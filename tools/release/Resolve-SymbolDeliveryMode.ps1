[CmdletBinding()]
param(
    [AllowEmptyString()][string]$Override = '',
    [AllowEmptyString()][string]$Source = '',
    [string]$StatePath = 'artifacts/release/state.json',
    [int]$TimeoutSec = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

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

function Test-DotNetSymbolSourceSupport {
    try {
        $output = (& dotnet nuget push --help 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0) {
            return [pscustomobject]@{ Supported = $false; SdkVersion = '' }
        }
        $sdkVersion = ''
        try {
            $sdkVersion = (& dotnet --version 2>$null | Select-Object -First 1)
        } catch {
            $sdkVersion = ''
        }
        return [pscustomobject]@{
            Supported  = ($output -match '--symbol-source')
            SdkVersion = [string]$sdkVersion
        }
    } catch {
        return [pscustomobject]@{ Supported = $false; SdkVersion = '' }
    }
}

$mode = 'manual'
$evidence = ''
$probeError = ''

if (-not [string]::IsNullOrWhiteSpace($Override)) {
    if (Test-SymbolDeliveryModeValue -Value $Override) {
        $mode = $Override.Trim().ToLowerInvariant()
        if ($mode -eq 'auto') {
            $evidence = '维护者显式声明运行环境支持符号自动交付'
        } else {
            $evidence = '维护者显式声明运行环境不支持符号自动交付'
        }
    } else {
        $mode = 'manual'
        $probeError = "非法覆盖值：$Override"
        $evidence = "判定异常：$probeError；已按不可自动推送处理"
    }
} else {
    try {
        $symbolPublishUrl = Get-ServiceIndexResourceUrl -Source $Source -ResourceTypePrefix 'SymbolPackagePublish' -TimeoutSec $TimeoutSec
        $sourceSupports = -not [string]::IsNullOrWhiteSpace($symbolPublishUrl)
        $client = Test-DotNetSymbolSourceSupport

        if ($sourceSupports -and $client.Supported) {
            $mode = 'auto'
            $evidence = "源与客户端均支持符号自动交付（资源 $symbolPublishUrl；dotnet $($client.SdkVersion)）"
        } elseif (-not $sourceSupports) {
            $mode = 'manual'
            $evidence = '源未声明符号发布资源（SymbolPackagePublish）'
        } else {
            $mode = 'manual'
            $evidence = '推送客户端不支持符号源参数（--symbol-source）'
        }
    } catch {
        $mode = 'manual'
        $probeError = $_.Exception.Message
        $evidence = "判定异常：$probeError；已按不可自动推送处理"
    }
}

$decisionMode = if ($mode -eq 'auto') { 'Auto' } else { 'Manual' }

$state = Read-ReleaseState -StatePath $StatePath
$state.SymbolDecision = [pscustomobject]@{
    Mode       = $decisionMode
    Evidence   = $evidence
    ProbeError = $probeError
}
Add-ReleaseStage -State $state -StageName 'SymbolResolution'
Write-ReleaseState -State $state -StatePath $StatePath

$singleLineEvidence = ($evidence -replace "`r|`n", ' ')
Add-ActionOutput -Name 'symbol_mode' -Value $mode
Add-ActionOutput -Name 'symbol_evidence' -Value $singleLineEvidence

Write-Output "[符号判定] 方式：$mode"
Write-Output "[符号判定] 依据：$singleLineEvidence"
exit 0