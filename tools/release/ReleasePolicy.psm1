# 规则中心：其他脚本禁止重复定义版本正则、包标识、源地址、重试上限与错误码等规则常量。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
} catch {
}

$script:ReleasePolicyConstants = [ordered]@{
    PackageId                   = 'S7.NetCore'
    OfficialPackageSource       = 'https://api.nuget.org/v3/index.json'
    OfficialFlatContainerSource = 'https://api.nuget.org/v3-flatcontainer'
    PackageTargetFramework      = 'net10.0'
    TargetConfiguration         = 'Release'
    MaxPushAttempts             = 3
    RetryDelaySeconds           = @(5, 15)
    VersionPattern              = '^v?(?<version>\d+\.\d+\.\d+)$'
    ApprovedSymbolDeliveryModes = @('auto', 'manual')
    StateSchemaVersion          = 1
    DefaultRequestTimeoutSec    = 30
}

$script:ReleaseErrorCatalog = @{
    TAG_FORMAT_INVALID      = [pscustomobject]@{ Stage = 'VersionValidation'; ExitCode = 1;  Remediation = '使用形如 v2.0.0 或 2.0.0 的正式版本标签重新发布' }
    VERSION_MISMATCH        = [pscustomobject]@{ Stage = 'VersionValidation'; ExitCode = 2;  Remediation = '使 Directory.Build.props 的 VersionPrefix 与标签完全一致后重跑' }
    PRERELEASE_REJECTED     = [pscustomobject]@{ Stage = 'VersionValidation'; ExitCode = 3;  Remediation = '移除预发布标记与连字符标识，改以正式版本发布' }
    EVENT_SKIPPED           = [pscustomobject]@{ Stage = 'Trigger';           ExitCode = 0;  Remediation = '无需处置；非发布事件不执行发布' }
    RESTORE_FAILED          = [pscustomobject]@{ Stage = 'Restore';           ExitCode = 10; Remediation = '检查依赖源可达性与依赖版本；修复后重跑' }
    BUILD_FAILED            = [pscustomobject]@{ Stage = 'Build';             ExitCode = 11; Remediation = '按编译错误摘要修复源码后重跑' }
    TEST_FAILED             = [pscustomobject]@{ Stage = 'Test';              ExitCode = 12; Remediation = '按失败用例清单修复后重跑' }
    TEST_TIMEOUT            = [pscustomobject]@{ Stage = 'Test';              ExitCode = 13; Remediation = '排查挂起用例或缩短用例耗时' }
    ARTIFACT_MISSING        = [pscustomobject]@{ Stage = 'ArtifactValidation'; ExitCode = 14; Remediation = '确认构建配置与文档生成开关后重跑' }
    PACK_FAILED             = [pscustomobject]@{ Stage = 'Pack';              ExitCode = 15; Remediation = '检查打包命令输出与输出目录' }
    METADATA_MISSING        = [pscustomobject]@{ Stage = 'PackageValidation'; ExitCode = 16; Remediation = '补齐项目文件中的包元数据' }
    PACKAGE_CONTENT_INVALID = [pscustomobject]@{ Stage = 'PackageValidation'; ExitCode = 17; Remediation = '移除违规内容项后重跑' }
    SYMBOL_PACK_FAILED      = [pscustomobject]@{ Stage = 'Pack';              ExitCode = 18; Remediation = '判定为 manual 时必须产出符号包；检查打包输出' }
    CREDENTIAL_MISSING      = [pscustomobject]@{ Stage = 'Push';              ExitCode = 20; Remediation = '配置仓库密钥 NUGET_API_KEY 后重跑' }
    PUSH_AUTH_FAILED        = [pscustomobject]@{ Stage = 'Push';              ExitCode = 21; Remediation = '更换具备推送权限的有效密钥（不重试）' }
    PUSH_REJECTED           = [pscustomobject]@{ Stage = 'Push';              ExitCode = 22; Remediation = '按源返回原因修正包内容或元数据' }
    PUSH_RETRY_EXHAUSTED    = [pscustomobject]@{ Stage = 'Push';              ExitCode = 23; Remediation = '源不可达或服务端错误；稍后重跑（已成功主包不会重推）' }
    PUSH_TIMEOUT            = [pscustomobject]@{ Stage = 'Push';              ExitCode = 24; Remediation = '检查网络与源状态后重跑' }
    SYMBOL_PUSH_FAILED      = [pscustomobject]@{ Stage = 'Push';              ExitCode = 25; Remediation = '主包已成功；仅修复符号包问题后重跑以补推' }
    CONCURRENCY_CONFLICT    = [pscustomobject]@{ Stage = 'Concurrency';       ExitCode = 26; Remediation = '等待同版本运行结束后重跑' }
    RUN_TIMEOUT             = [pscustomobject]@{ Stage = 'Run';               ExitCode = 27; Remediation = '检查超时阶段，优化耗时后重跑' }
}

function ConvertFrom-ReleaseTag {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Tag = ''
    )

    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return [pscustomobject]@{ Success = $false; Version = $null; Reason = '标签为空' }
    }

    $trimmed = $Tag.Trim()
    if ($trimmed.Contains('-')) {
        return [pscustomobject]@{ Success = $false; Version = $null; Reason = "标签含连字符，属于预发布版本：$trimmed" }
    }

    $match = [regex]::Match($trimmed, $script:ReleasePolicyConstants.VersionPattern)
    if (-not $match.Success) {
        return [pscustomobject]@{ Success = $false; Version = $null; Reason = "标签格式非法：$trimmed；合法示例：v2.0.0 或 2.0.0" }
    }

    return [pscustomobject]@{ Success = $true; Version = $match.Groups['version'].Value; Reason = '' }
}

function Get-VersionPrefix {
    [CmdletBinding()]
    param(
        [string]$RepositoryRoot = '.'
    )

    $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $propsPath)) {
        throw "未找到 Directory.Build.props：$propsPath"
    }

    $content = Get-Content -Raw -LiteralPath $propsPath
    $match = [regex]::Match($content, '<VersionPrefix>\s*(?<value>[^<]+?)\s*</VersionPrefix>')
    if (-not $match.Success) {
        throw 'Directory.Build.props 未声明 VersionPrefix'
    }

    return $match.Groups['value'].Value.Trim()
}

function Assert-VersionConsistency {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$VersionPrefix
    )

    $normalizedVersion = $Version.Trim()
    $normalizedPrefix = $VersionPrefix.Trim()
    return [pscustomobject]@{
        Consistent    = ($normalizedVersion -ceq $normalizedPrefix)
        Version       = $normalizedVersion
        VersionPrefix = $normalizedPrefix
    }
}

function Test-SymbolDeliveryModeValue {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Value = ''
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return ($script:ReleasePolicyConstants.ApprovedSymbolDeliveryModes -ccontains $Value.Trim())
}

function Get-PushFailureKind {
    [CmdletBinding()]
    param(
        [int]$StatusCode = 0,
        [AllowEmptyString()][string]$ErrorMessage = '',
        [bool]$TimedOut = $false
    )

    if ($StatusCode -eq 409) {
        return [pscustomobject]@{ Kind = 'VersionExists'; Retryable = $false; AlreadyExists = $true }
    }
    if ($StatusCode -eq 401 -or $StatusCode -eq 403) {
        return [pscustomobject]@{ Kind = 'Authentication'; Retryable = $false; AlreadyExists = $false }
    }
    if ($StatusCode -ge 500 -and $StatusCode -le 599) {
        return [pscustomobject]@{ Kind = 'Server'; Retryable = $true; AlreadyExists = $false }
    }
    if ($StatusCode -eq 408 -or $TimedOut) {
        return [pscustomobject]@{ Kind = 'Timeout'; Retryable = $true; AlreadyExists = $false }
    }
    if ($StatusCode -gt 0 -and $StatusCode -lt 500) {
        return [pscustomobject]@{ Kind = 'Rejected'; Retryable = $false; AlreadyExists = $false }
    }
    if ($StatusCode -eq 0) {
        if ($ErrorMessage -match 'timed?\s*out|timeout|canceled|cancelled') {
            return [pscustomobject]@{ Kind = 'Timeout'; Retryable = $true; AlreadyExists = $false }
        }
        return [pscustomobject]@{ Kind = 'Network'; Retryable = $true; AlreadyExists = $false }
    }
    return [pscustomobject]@{ Kind = 'Unknown'; Retryable = $false; AlreadyExists = $false }
}

function Get-ReleaseErrorInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Code
    )

    if (-not $script:ReleaseErrorCatalog.ContainsKey($Code)) {
        throw "未知错误码：$Code"
    }
    return $script:ReleaseErrorCatalog[$Code]
}

function New-ReleaseError {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [AllowEmptyString()][string]$Stage = '',
        [AllowEmptyString()][string]$Message = '',
        [AllowEmptyString()][string]$Remediation = ''
    )

    $entry = Get-ReleaseErrorInfo -Code $Code
    if ([string]::IsNullOrWhiteSpace($Stage)) {
        $Stage = $entry.Stage
    }
    if ([string]::IsNullOrWhiteSpace($Remediation)) {
        $Remediation = $entry.Remediation
    }

    return [pscustomobject]@{
        Code        = $Code
        Stage       = $Stage
        Message     = $Message
        Remediation = $Remediation
    }
}

function Read-ReleaseState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$StatePath
    )

    $schemaVersion = $script:ReleasePolicyConstants.StateSchemaVersion
    $defaults = [ordered]@{
        SchemaVersion    = $schemaVersion
        StartedAtUtc     = (Get-Date).ToUniversalTime().ToString('o')
        CompletedAtUtc   = $null
        CompletedStages  = @()
        FailedStage      = $null
        Error            = $null
        Request          = $null
        Version          = $null
        SymbolDecision   = $null
        BuildArtifacts   = @()
        BuildDiagnostics = $null
        Tests            = $null
        ExistenceChecks  = @()
        Packages         = @()
        MainPush         = $null
        SymbolPush       = $null
    }

    if (-not (Test-Path -LiteralPath $StatePath)) {
        return [pscustomobject]$defaults
    }

    $raw = [IO.File]::ReadAllText($StatePath, [Text.UTF8Encoding]::new($false))
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [pscustomobject]$defaults
    }

    $state = $raw | ConvertFrom-Json
    $schemaProperty = $state.PSObject.Properties['SchemaVersion']
    if ($null -eq $schemaProperty -or $schemaProperty.Value -ne $schemaVersion) {
        throw "state.json 的 SchemaVersion 与预期 $schemaVersion 不一致"
    }

    foreach ($key in $defaults.Keys) {
        if ($null -eq $state.PSObject.Properties[$key]) {
            $state | Add-Member -NotePropertyName $key -NotePropertyValue $defaults[$key] -Force
        }
    }
    return $state
}

function Write-ReleaseState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$StatePath
    )

    $State.SchemaVersion = $script:ReleasePolicyConstants.StateSchemaVersion

    $directory = Split-Path -Parent $StatePath
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $State | ConvertTo-Json -Depth 20
    $tempPath = "$StatePath.tmp"
    [IO.File]::WriteAllText($tempPath, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tempPath -Destination $StatePath -Force
}

function Add-ReleaseStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$StageName
    )

    $stages = @()
    if ($null -ne $State.CompletedStages) {
        $stages = @($State.CompletedStages)
    }
    if (-not ($stages -contains $StageName)) {
        $State.CompletedStages = $stages + $StageName
    }
}

function Write-ReleaseFailure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$StatePath,
        [Parameter(Mandatory = $true)][string]$Code,
        [AllowEmptyString()][string]$Stage = '',
        [AllowEmptyString()][string]$Message = ''
    )

    $state = Read-ReleaseState -StatePath $StatePath
    $errorRecord = New-ReleaseError -Code $Code -Stage $Stage -Message $Message
    $state.Error = $errorRecord
    $state.FailedStage = $errorRecord.Stage
    $state.CompletedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Write-ReleaseState -State $state -StatePath $StatePath
    return $errorRecord
}

function Get-FlatContainerBaseUrl {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Source = ''
    )

    if ([string]::IsNullOrWhiteSpace($Source) -or $Source -eq $script:ReleasePolicyConstants.OfficialPackageSource) {
        return $script:ReleasePolicyConstants.OfficialFlatContainerSource
    }

    $uri = [uri]$Source
    return "$($uri.Scheme)://$($uri.Authority)/v3-flatcontainer"
}

function Get-ServiceIndexResourceUrl {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Source = '',
        [AllowEmptyString()][string]$ResourceType = '',
        [AllowEmptyString()][string]$ResourceTypePrefix = '',
        [int]$TimeoutSec = 30
    )

    if ([string]::IsNullOrWhiteSpace($Source)) {
        $Source = $script:ReleasePolicyConstants.OfficialPackageSource
    }

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    try {
        $json = $client.GetStringAsync($Source).GetAwaiter().GetResult()
        $index = $json | ConvertFrom-Json
        $resources = @()
        if ($null -ne $index.PSObject.Properties['resources']) {
            $resources = @($index.resources)
        }

        $match = $null
        if (-not [string]::IsNullOrWhiteSpace($ResourceType)) {
            $match = $resources | Where-Object { $_.'@type' -eq $ResourceType } | Select-Object -First 1
        } elseif (-not [string]::IsNullOrWhiteSpace($ResourceTypePrefix)) {
            $match = $resources | Where-Object { $_.'@type' -like "$ResourceTypePrefix*" } | Select-Object -First 1
        }

        if ($null -eq $match) {
            return $null
        }
        return $match.'@id'
    } finally {
        $client.Dispose()
    }
}

function Test-NuGetPackageExistsCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PackageId,
        [Parameter(Mandatory = $true)][string]$Version,
        [AllowEmptyString()][string]$Source = '',
        [int]$TimeoutSec = 30
    )

    if ([string]::IsNullOrWhiteSpace($Source)) {
        $Source = $script:ReleasePolicyConstants.OfficialPackageSource
    }

    $flatContainer = Get-FlatContainerBaseUrl -Source $Source
    $id = $PackageId.ToLowerInvariant()
    $normalizedVersion = $Version.ToLowerInvariant()
    $url = "$flatContainer/$id/$normalizedVersion/$id.$normalizedVersion.nupkg"

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    try {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Head, $url)
        try {
            $response = $client.SendAsync($request).GetAwaiter().GetResult()
            $statusCode = [int]$response.StatusCode
            $response.Dispose()
            return [pscustomobject]@{
                Exists       = ($statusCode -eq 200)
                StatusCode   = $statusCode
                ErrorMessage = ''
            }
        } catch [System.Threading.Tasks.TaskCanceledException] {
            return [pscustomobject]@{ Exists = $false; StatusCode = 0; ErrorMessage = '请求超时' }
        } catch {
            return [pscustomobject]@{ Exists = $false; StatusCode = 0; ErrorMessage = $_.Exception.Message }
        } finally {
            $request.Dispose()
        }
    } finally {
        $client.Dispose()
    }
}

Export-ModuleMember -Variable 'ReleasePolicyConstants' -Function @(
    'ConvertFrom-ReleaseTag'
    'Get-VersionPrefix'
    'Assert-VersionConsistency'
    'Test-SymbolDeliveryModeValue'
    'Get-PushFailureKind'
    'Get-ReleaseErrorInfo'
    'New-ReleaseError'
    'Read-ReleaseState'
    'Write-ReleaseState'
    'Add-ReleaseStage'
    'Write-ReleaseFailure'
    'Get-FlatContainerBaseUrl'
    'Get-ServiceIndexResourceUrl'
    'Test-NuGetPackageExistsCore'
)