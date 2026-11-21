[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [AllowEmptyString()][string]$SymbolPackagePath = '',
    [Parameter(Mandatory = $true)][ValidateSet('auto', 'manual')][string]$SymbolMode,
    [Parameter(Mandatory = $true)][string]$Version,
    [AllowEmptyString()][string]$Source = '',
    [string]$StatePath = 'artifacts/release/state.json',
    [int]$MaxAttempts = 0,
    [int]$TimeoutSec = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = $ReleasePolicyConstants.OfficialPackageSource
}
if ($MaxAttempts -le 0) {
    $MaxAttempts = $ReleasePolicyConstants.MaxPushAttempts
}
if ($TimeoutSec -le 0) {
    $TimeoutSec = 100
}

function Exit-WithFailure {
    param([string]$Code, [string]$Message)

    $errorRecord = Write-ReleaseFailure -StatePath $StatePath -Code $Code -Message $Message
    Write-Output "[推送] 失败：$Message"
    Write-Output "[推送] 处置建议：$($errorRecord.Remediation)"
    exit (Get-ReleaseErrorInfo -Code $Code).ExitCode
}

function Add-ExistenceCheck {
    param([bool]$Exists, [int]$StatusCode)

    $state = Read-ReleaseState -StatePath $StatePath
    $checks = @()
    if ($null -ne $state.ExistenceChecks) {
        $checks = @($state.ExistenceChecks)
    }
    $checks += [pscustomobject]@{
        PackageId    = $ReleasePolicyConstants.PackageId
        Version      = $Version
        Phase        = 'PublishPrecheck'
        Exists       = $Exists
        StatusCode   = $StatusCode
        CheckedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    $state.ExistenceChecks = $checks
    Write-ReleaseState -State $state -StatePath $StatePath
}

function Invoke-PushAttempt {
    param(
        [string]$Url,
        [string]$FilePath,
        [string]$ApiKey,
        [int]$AttemptTimeoutSec
    )

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds($AttemptTimeoutSec)
    try {
        $stream = [IO.File]::OpenRead($FilePath)
        try {
            $multipart = [System.Net.Http.MultipartFormDataContent]::new()
            $fileContent = [System.Net.Http.StreamContent]::new($stream)
            $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
            $multipart.Add($fileContent, 'package', [IO.Path]::GetFileName($FilePath))

            $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, $Url)
            $request.Headers.Add('X-NuGet-ApiKey', $ApiKey)
            $request.Content = $multipart
            try {
                $response = $client.SendAsync($request).GetAwaiter().GetResult()
                $statusCode = [int]$response.StatusCode
                $body = ''
                try {
                    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                } catch {
                    $body = ''
                }
                $response.Dispose()
                return [pscustomobject]@{ StatusCode = $statusCode; TimedOut = $false; ErrorMessage = ''; Body = $body }
            } catch [System.Threading.Tasks.TaskCanceledException] {
                return [pscustomobject]@{ StatusCode = 0; TimedOut = $true; ErrorMessage = '请求超时'; Body = '' }
            } catch {
                return [pscustomobject]@{ StatusCode = 0; TimedOut = $false; ErrorMessage = $_.Exception.Message; Body = '' }
            } finally {
                $request.Dispose()
                $multipart.Dispose()
            }
        } finally {
            $stream.Dispose()
        }
    } finally {
        $client.Dispose()
    }
}

function Invoke-PushWithRetry {
    param(
        [string]$Url,
        [string]$FilePath,
        [string]$ApiKey,
        [int]$AttemptLimit
    )

    $attempts = @()
    for ($attempt = 1; $attempt -le $AttemptLimit; $attempt++) {
        $result = Invoke-PushAttempt -Url $Url -FilePath $FilePath -ApiKey $ApiKey -AttemptTimeoutSec $TimeoutSec
        $kind = Get-PushFailureKind -StatusCode $result.StatusCode -ErrorMessage $result.ErrorMessage -TimedOut $result.TimedOut
        $attempts += [pscustomobject]@{
            AttemptNumber = $attempt
            FailureKind   = $kind.Kind
            StatusCode    = $result.StatusCode
            OccurredAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        }

        if ($result.StatusCode -ge 200 -and $result.StatusCode -lt 300) {
            return [pscustomobject]@{ Outcome = 'Pushed'; Attempts = @($attempts); LastResult = $result; FailureKind = $kind }
        }
        if ($kind.AlreadyExists) {
            return [pscustomobject]@{ Outcome = 'AlreadyExists'; Attempts = @($attempts); LastResult = $result; FailureKind = $kind }
        }
        if (-not $kind.Retryable -or $attempt -ge $AttemptLimit) {
            return [pscustomobject]@{ Outcome = 'Failed'; Attempts = @($attempts); LastResult = $result; FailureKind = $kind }
        }

        $delayIndex = [Math]::Min($attempt - 1, $ReleasePolicyConstants.RetryDelaySeconds.Count - 1)
        Write-Output "[推送] 第 $attempt 次失败（$($kind.Kind)），$($ReleasePolicyConstants.RetryDelaySeconds[$delayIndex]) 秒后重试"
        Start-Sleep -Seconds $ReleasePolicyConstants.RetryDelaySeconds[$delayIndex]
    }
}

function Get-PushFailureCode {
    param($FailureKind)

    if ($null -eq $FailureKind) {
        return 'PUSH_RETRY_EXHAUSTED'
    }
    switch ($FailureKind.Kind) {
        'Authentication' { return 'PUSH_AUTH_FAILED' }
        'Rejected' { return 'PUSH_REJECTED' }
        'Timeout' { return 'PUSH_TIMEOUT' }
        default { return 'PUSH_RETRY_EXHAUSTED' }
    }
}

$apiKey = $env:NUGET_API_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Output '[推送] NuGet API key：未配置'
    Exit-WithFailure -Code 'CREDENTIAL_MISSING' -Message '未配置 NUGET_API_KEY'
}
Write-Output '[推送] NuGet API key：已配置'

if (-not (Test-Path -LiteralPath $PackagePath)) {
    Exit-WithFailure -Code 'PACK_FAILED' -Message "主包文件不存在：$([IO.Path]::GetFileName($PackagePath))"
}
$packageFullPath = (Resolve-Path -LiteralPath $PackagePath).Path

$precheck = Test-NuGetPackageExistsCore -PackageId $ReleasePolicyConstants.PackageId -Version $Version -Source $Source -TimeoutSec $ReleasePolicyConstants.DefaultRequestTimeoutSec
if ($precheck.StatusCode -ne 200 -and $precheck.StatusCode -ne 404) {
    $detail = if ($precheck.StatusCode -eq 0) { $precheck.ErrorMessage } else { "HTTP $($precheck.StatusCode)" }
    Exit-WithFailure -Code 'PUSH_RETRY_EXHAUSTED' -Message "推送前存在性复核失败：$detail"
}
Add-ExistenceCheck -Exists $precheck.Exists -StatusCode $precheck.StatusCode
$mainExists = $precheck.Exists

if ($mainExists -and $SymbolMode -eq 'auto') {
    $now = (Get-Date).ToUniversalTime().ToString('o')
    $state = Read-ReleaseState -StatePath $StatePath
    $state.MainPush = [pscustomobject]@{
        Target        = 'Main'
        Outcome       = 'AlreadyExists'
        Attempts      = @()
        CompletedAtUtc = $now
        Note          = '版本已存在，未重复推送'
    }
    $state.SymbolPush = [pscustomobject]@{
        Target        = 'Symbol'
        Outcome       = 'Skipped'
        Attempts      = @()
        CompletedAtUtc = $now
        Note          = '符号随主包分发'
    }
    $state.CompletedAtUtc = $now
    Add-ReleaseStage -State $state -StageName 'Push'
    Write-ReleaseState -State $state -StatePath $StatePath

    Write-Output "[推送] 版本 $Version 已存在，未重复推送；符号随主包分发"
    exit 0
}

$mainPushUrl = $null
try {
    $mainPushUrl = Get-ServiceIndexResourceUrl -Source $Source -ResourceType 'PackagePublish/2.0.0' -TimeoutSec $ReleasePolicyConstants.DefaultRequestTimeoutSec
} catch {
    $mainPushUrl = $null
}
if ([string]::IsNullOrWhiteSpace($mainPushUrl)) {
    Exit-WithFailure -Code 'PUSH_RETRY_EXHAUSTED' -Message '官方源未声明 PackagePublish/2.0.0 资源'
}

$mainOutcome = 'AlreadyExists'
$mainAttempts = @()
if (-not $mainExists) {
    $mainResult = Invoke-PushWithRetry -Url $mainPushUrl -FilePath $packageFullPath -ApiKey $apiKey -AttemptLimit $MaxAttempts
    $mainOutcome = $mainResult.Outcome
    $mainAttempts = @($mainResult.Attempts)
}

$now = (Get-Date).ToUniversalTime().ToString('o')
$state = Read-ReleaseState -StatePath $StatePath
$state.MainPush = [pscustomobject]@{
    Target         = 'Main'
    Outcome        = $mainOutcome
    Attempts       = @($mainAttempts)
    CompletedAtUtc = $now
    Note           = if ($mainOutcome -eq 'AlreadyExists') { '版本已存在，未重复推送' } else { '' }
}

if ($mainOutcome -eq 'Failed') {
    $failureCode = Get-PushFailureCode -FailureKind $mainResult.FailureKind
    $state.CompletedAtUtc = $now
    Write-ReleaseState -State $state -StatePath $StatePath
    $detail = if ($mainResult.LastResult.StatusCode -gt 0) { "HTTP $($mainResult.LastResult.StatusCode)" } else { $mainResult.LastResult.ErrorMessage }
    Exit-WithFailure -Code $failureCode -Message "主包推送失败：$detail"
}

if ($mainOutcome -eq 'Pushed') {
    Write-Output "[推送] 主包推送成功（尝试 $(@($mainAttempts).Count) 次）"
} else {
    Write-Output '[推送] 主包已存在，跳过主包推送'
}

if ($SymbolMode -eq 'auto') {
    $state.SymbolPush = [pscustomobject]@{
        Target         = 'Symbol'
        Outcome        = 'Skipped'
        Attempts       = @()
        CompletedAtUtc = $now
        Note           = '符号随主包分发'
    }
    $state.CompletedAtUtc = $now
    Add-ReleaseStage -State $state -StageName 'Push'
    Write-ReleaseState -State $state -StatePath $StatePath
    Write-Output '[推送] auto 模式未构造独立符号包推送请求；符号随主包分发'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($SymbolPackagePath) -or -not (Test-Path -LiteralPath $SymbolPackagePath)) {
    $state.CompletedAtUtc = $now
    Write-ReleaseState -State $state -StatePath $StatePath
    Exit-WithFailure -Code 'SYMBOL_PACK_FAILED' -Message 'manual 模式缺少符号包文件'
}
$symbolFullPath = (Resolve-Path -LiteralPath $SymbolPackagePath).Path

$symbolPushUrl = $null
try {
    $symbolPushUrl = Get-ServiceIndexResourceUrl -Source $Source -ResourceTypePrefix 'SymbolPackagePublish' -TimeoutSec $ReleasePolicyConstants.DefaultRequestTimeoutSec
} catch {
    $symbolPushUrl = $null
}
if ([string]::IsNullOrWhiteSpace($symbolPushUrl)) {
    $state.CompletedAtUtc = $now
    Write-ReleaseState -State $state -StatePath $StatePath
    Exit-WithFailure -Code 'SYMBOL_PUSH_FAILED' -Message '官方源未声明符号包发布资源'
}

$symbolResult = Invoke-PushWithRetry -Url $symbolPushUrl -FilePath $symbolFullPath -ApiKey $apiKey -AttemptLimit $MaxAttempts
$symbolNow = (Get-Date).ToUniversalTime().ToString('o')
$state.SymbolPush = [pscustomobject]@{
    Target         = 'Symbol'
    Outcome        = $symbolResult.Outcome
    Attempts       = @($symbolResult.Attempts)
    CompletedAtUtc = $symbolNow
    Note           = if ($symbolResult.Outcome -eq 'AlreadyExists') { '符号包已存在' } else { '' }
}

if ($symbolResult.Outcome -eq 'Failed') {
    $state.CompletedAtUtc = $symbolNow
    Write-ReleaseState -State $state -StatePath $StatePath
    $detail = if ($symbolResult.LastResult.StatusCode -gt 0) { "HTTP $($symbolResult.LastResult.StatusCode)" } else { $symbolResult.LastResult.ErrorMessage }
    Exit-WithFailure -Code 'SYMBOL_PUSH_FAILED' -Message "主包已推送成功、符号包推送失败：$detail"
}

$state.CompletedAtUtc = $symbolNow
Add-ReleaseStage -State $state -StageName 'Push'
Write-ReleaseState -State $state -StatePath $StatePath

if ($symbolResult.Outcome -eq 'Pushed') {
    Write-Output "[推送] 符号包推送成功（尝试 $(@($symbolResult.Attempts).Count) 次）"
} else {
    Write-Output '[推送] 符号包已存在，未重复推送'
}
exit 0