[CmdletBinding()]
param(
    [AllowEmptyString()][string]$PackageId = '',
    [Parameter(Mandatory = $true)][string]$Version,
    [AllowEmptyString()][string]$Source = '',
    [string]$StatePath = 'artifacts/release/state.json',
    [int]$TimeoutSec = 30,
    [string]$Phase = 'Preflight'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

if ([string]::IsNullOrWhiteSpace($PackageId)) {
    $PackageId = $ReleasePolicyConstants.PackageId
}
if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = $ReleasePolicyConstants.OfficialPackageSource
}
if ($TimeoutSec -le 0) {
    $TimeoutSec = $ReleasePolicyConstants.DefaultRequestTimeoutSec
}

function Add-ActionOutput {
    param([string]$Name, [string]$Value)
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        return
    }
    $line = "$Name=$Value" + "`n"
    [IO.File]::AppendAllText($env:GITHUB_OUTPUT, $line, [Text.UTF8Encoding]::new($false))
}

function Add-ExistenceCheck {
    param([bool]$Exists, [int]$StatusCode)

    $state = Read-ReleaseState -StatePath $StatePath
    $checks = @()
    if ($null -ne $state.ExistenceChecks) {
        $checks = @($state.ExistenceChecks)
    }
    $checks += [pscustomobject]@{
        PackageId    = $PackageId
        Version      = $Version
        Phase        = $Phase
        Exists       = $Exists
        StatusCode   = $StatusCode
        CheckedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    $state.ExistenceChecks = $checks
    Add-ReleaseStage -State $state -StageName 'ExistenceCheck'
    Write-ReleaseState -State $state -StatePath $StatePath
}

$probe = Test-NuGetPackageExistsCore -PackageId $PackageId -Version $Version -Source $Source -TimeoutSec $TimeoutSec
if ($probe.StatusCode -ne 200 -and $probe.StatusCode -ne 404) {
    $detail = if ($probe.StatusCode -eq 0) { $probe.ErrorMessage } else { "HTTP $($probe.StatusCode)" }
    $errorRecord = Write-ReleaseFailure -StatePath $StatePath -Code 'PUSH_RETRY_EXHAUSTED' -Message "存在性查询失败：$detail"
    Write-Output "[存在性检查] 失败：$detail"
    Write-Output "[存在性检查] 处置建议：$($errorRecord.Remediation)"
    exit (Get-ReleaseErrorInfo -Code 'PUSH_RETRY_EXHAUSTED').ExitCode
}

Add-ExistenceCheck -Exists $probe.Exists -StatusCode $probe.StatusCode
$existsText = $probe.Exists.ToString().ToLowerInvariant()
Add-ActionOutput -Name 'exists' -Value $existsText

Write-Output "[存在性检查] $PackageId $Version -> exists=$existsText（HTTP $($probe.StatusCode)，阶段 $Phase）"
exit 0