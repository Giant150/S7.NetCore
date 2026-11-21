[CmdletBinding()]
param(
    [string]$RepositoryRoot = '.',
    [AllowEmptyString()][string]$Configuration = '',
    [AllowEmptyString()][string]$TargetFramework = '',
    [string]$StatePath = 'artifacts/release/state.json',
    [AllowEmptyString()][string]$BuildLogPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

if ([string]::IsNullOrWhiteSpace($Configuration)) {
    $Configuration = $ReleasePolicyConstants.TargetConfiguration
}
if ([string]::IsNullOrWhiteSpace($TargetFramework)) {
    $TargetFramework = $ReleasePolicyConstants.PackageTargetFramework
}

$repositoryRootResolved = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$outputDirectory = Join-Path $repositoryRootResolved "src/S7.NetCore/bin/$Configuration/$TargetFramework"

$expectedFiles = @(
    'S7.NetCore.dll'
    'S7.NetCore.xml'
)

$artifacts = @()
$missing = @()

function Get-RelativePathFromRepository {
    param([string]$BasePath, [string]$TargetPath)

    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/')
    $target = [IO.Path]::GetFullPath($TargetPath)
    if ($target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) {
        return $target.Substring($base.Length).TrimStart('\', '/').Replace('\', '/')
    }
    return $target.Replace('\', '/')
}

foreach ($fileName in $expectedFiles) {
    $fullPath = Join-Path $outputDirectory $fileName
    if (-not (Test-Path -LiteralPath $fullPath)) {
        $missing += Get-RelativePathFromRepository -BasePath $repositoryRootResolved -TargetPath $fullPath
        continue
    }
    $item = Get-Item -LiteralPath $fullPath
    $artifacts += [pscustomobject]@{
        FileName     = $item.Name
        RelativePath = Get-RelativePathFromRepository -BasePath $repositoryRootResolved -TargetPath $item.FullName
        SizeBytes    = [long]$item.Length
    }
}

if ($missing.Count -gt 0) {
    $errorRecord = Write-ReleaseFailure -StatePath $StatePath -Code 'ARTIFACT_MISSING' -Message "缺失产物：$($missing -join ', ')"
    Write-Output "[产物门禁] 失败：缺失 $($missing -join ', ')"
    Write-Output "[产物门禁] 处置建议：$($errorRecord.Remediation)"
    exit (Get-ReleaseErrorInfo -Code 'ARTIFACT_MISSING').ExitCode
}

$warningCount = 0
$warningSamples = @()
if (-not [string]::IsNullOrWhiteSpace($BuildLogPath) -and (Test-Path -LiteralPath $BuildLogPath)) {
    $warningLines = @(Get-Content -LiteralPath $BuildLogPath | Where-Object { $_ -match ': warning ' })
    $warningCount = $warningLines.Count
    $warningSamples = @($warningLines | Select-Object -First 20)
}

$diagnostics = [pscustomobject]@{
    WarningCount       = $warningCount
    WarningSamples     = $warningSamples
    NativeBridgeWaiver = 'S7.NetCore.Tests.Native 保留 TreatWarningsAsErrors=false 与 NoWarn（生成代码互操作），不参与打包'
}

$state = Read-ReleaseState -StatePath $StatePath
$state.BuildArtifacts = $artifacts
$state.BuildDiagnostics = $diagnostics
Add-ReleaseStage -State $state -StageName 'ArtifactValidation'
Write-ReleaseState -State $state -StatePath $StatePath

foreach ($artifact in $artifacts) {
    Write-Output "[产物门禁] $($artifact.RelativePath) ($($artifact.SizeBytes) bytes)"
}
Write-Output "[产物门禁] 通过：$($artifacts.Count) 个必备产物，编译警告 $warningCount 个"
exit 0