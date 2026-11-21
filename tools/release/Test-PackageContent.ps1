[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [AllowEmptyString()][string]$SymbolPackagePath = '',
    [Parameter(Mandatory = $true)][string]$ExpectedId,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [AllowEmptyString()][string]$TargetFramework = '',
    [string]$StatePath = 'artifacts/release/state.json',
    [AllowEmptyString()][string]$SymbolMode = '',
    [AllowEmptyString()][string]$RepositoryRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

if ([string]::IsNullOrWhiteSpace($TargetFramework)) {
    $TargetFramework = $ReleasePolicyConstants.PackageTargetFramework
}
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}
$repositoryRootResolved = (Resolve-Path -LiteralPath $RepositoryRoot).Path

function Exit-WithFailure {
    param([string]$Code, [string]$Message)

    $errorRecord = Write-ReleaseFailure -StatePath $StatePath -Code $Code -Message $Message
    Write-Output "[包校验] 失败：$Message"
    Write-Output "[包校验] 处置建议：$($errorRecord.Remediation)"
    exit (Get-ReleaseErrorInfo -Code $Code).ExitCode
}

function Read-PackageEntries {
    param([string]$PackageFullPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackageFullPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.FullName -like '*.nuspec' })
        if ($nuspecEntries.Count -eq 0) {
            return [pscustomobject]@{ Entries = $entries; NuspecText = '' }
        }
        $stream = $nuspecEntries[0].Open()
        try {
            $reader = [IO.StreamReader]::new($stream)
            try {
                $nuspecText = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }
        } finally {
            $stream.Dispose()
        }
        return [pscustomobject]@{ Entries = $entries; NuspecText = $nuspecText }
    } finally {
        $archive.Dispose()
    }
}

function Get-NuspecValue {
    param($MetadataNode, [string]$Name)

    $node = $MetadataNode.SelectSingleNode("./*[local-name()='$Name']")
    if ($null -eq $node) {
        return ''
    }
    if (-not [string]::IsNullOrWhiteSpace($node.InnerText)) {
        return $node.InnerText.Trim()
    }
    return ''
}

function Get-ContentViolations {
    param([string[]]$Entries, [string[]]$AllowedPatterns, [string[]]$ForbiddenPatterns)

    $violations = @()
    foreach ($entry in $Entries) {
        $allowed = $false
        foreach ($pattern in $AllowedPatterns) {
            if ($entry -match $pattern) {
                $allowed = $true
                break
            }
        }
        $forbidden = $false
        foreach ($pattern in $ForbiddenPatterns) {
            if ($entry -match $pattern) {
                $forbidden = $true
                break
            }
        }
        if (-not $allowed -or $forbidden) {
            $violations += $entry
        }
    }
    return $violations
}

$state = Read-ReleaseState -StatePath $StatePath
$effectiveSymbolMode = $SymbolMode
if ([string]::IsNullOrWhiteSpace($effectiveSymbolMode) -and $null -ne $state.SymbolDecision) {
    $effectiveSymbolMode = ([string]$state.SymbolDecision.Mode).ToLowerInvariant()
}
if ([string]::IsNullOrWhiteSpace($effectiveSymbolMode)) {
    $effectiveSymbolMode = 'manual'
}

$packageFullPath = (Resolve-Path -LiteralPath $PackagePath).Path
$expectedMainFileName = "$ExpectedId.$ExpectedVersion.nupkg"
$actualMainFileName = [IO.Path]::GetFileName($packageFullPath)
if ($actualMainFileName -ne $expectedMainFileName) {
    Exit-WithFailure -Code 'METADATA_MISSING' -Message "主包文件名 $actualMainFileName 与预期 $expectedMainFileName 不一致"
}

$mainPackage = Read-PackageEntries -PackageFullPath $packageFullPath
if ([string]::IsNullOrWhiteSpace($mainPackage.NuspecText)) {
    Exit-WithFailure -Code 'METADATA_MISSING' -Message '主包缺少 nuspec 文件'
}

[xml]$mainNuspec = $mainPackage.NuspecText
$mainMetadata = $mainNuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
if ($null -eq $mainMetadata) {
    Exit-WithFailure -Code 'METADATA_MISSING' -Message 'nuspec 缺少 metadata 节点'
}

$mainId = Get-NuspecValue -MetadataNode $mainMetadata -Name 'id'
$mainVersion = Get-NuspecValue -MetadataNode $mainMetadata -Name 'version'
$description = Get-NuspecValue -MetadataNode $mainMetadata -Name 'description'
$authors = Get-NuspecValue -MetadataNode $mainMetadata -Name 'authors'
$license = Get-NuspecValue -MetadataNode $mainMetadata -Name 'license'
$projectUrl = Get-NuspecValue -MetadataNode $mainMetadata -Name 'projectUrl'
$tags = Get-NuspecValue -MetadataNode $mainMetadata -Name 'tags'

$repositoryNode = $mainMetadata.SelectSingleNode("./*[local-name()='repository']")
$repositoryUrl = if ($null -ne $repositoryNode) { $repositoryNode.GetAttribute('url') } else { '' }

$dependencyVersion = ''
foreach ($dependencyNode in @($mainMetadata.SelectNodes(".//*[local-name()='dependency']"))) {
    if ($dependencyNode.GetAttribute('id') -eq 'Microsoft.Extensions.Logging.Abstractions') {
        $dependencyVersion = $dependencyNode.GetAttribute('version')
        break
    }
}

$expectedDependencyVersion = ''
$projectPath = Join-Path $repositoryRootResolved 'src/S7.NetCore/S7.NetCore.csproj'
if (Test-Path -LiteralPath $projectPath) {
    [xml]$projectDocument = Get-Content -Raw -LiteralPath $projectPath
    foreach ($reference in @($projectDocument.SelectNodes("//*[local-name()='PackageReference']"))) {
        if ($reference.GetAttribute('Include') -eq 'Microsoft.Extensions.Logging.Abstractions') {
            $expectedDependencyVersion = $reference.GetAttribute('Version')
            break
        }
    }
}

$missingMetadata = @()
if ($mainId -ne $ExpectedId) { $missingMetadata += "id=$mainId（预期 $ExpectedId）" }
if ($mainVersion -ne $ExpectedVersion) { $missingMetadata += "version=$mainVersion（预期 $ExpectedVersion）" }
if ([string]::IsNullOrWhiteSpace($description)) { $missingMetadata += 'description' }
if ([string]::IsNullOrWhiteSpace($authors)) { $missingMetadata += 'authors' }
if ([string]::IsNullOrWhiteSpace($license)) { $missingMetadata += 'license' }
if ([string]::IsNullOrWhiteSpace($projectUrl)) { $missingMetadata += 'projectUrl' }
if ([string]::IsNullOrWhiteSpace($repositoryUrl)) { $missingMetadata += 'repository' }
if ([string]::IsNullOrWhiteSpace($tags)) { $missingMetadata += 'tags' }
if ([string]::IsNullOrWhiteSpace($dependencyVersion)) {
    $missingMetadata += 'dependency:Microsoft.Extensions.Logging.Abstractions'
} elseif (-not [string]::IsNullOrWhiteSpace($expectedDependencyVersion) -and $dependencyVersion -ne $expectedDependencyVersion) {
    $missingMetadata += "dependency:Microsoft.Extensions.Logging.Abstractions=$dependencyVersion（预期 $expectedDependencyVersion）"
}

if ($missingMetadata.Count -gt 0) {
    Exit-WithFailure -Code 'METADATA_MISSING' -Message "元数据缺失或不一致：$($missingMetadata -join ', ')"
}

$escapedId = [regex]::Escape($ExpectedId)
$escapedFramework = [regex]::Escape($TargetFramework)
$mainAllowedPatterns = @(
    '^\[Content_Types\]\.xml$'
    '^_rels/\.rels$'
    ('^' + $escapedId + '\.nuspec$')
    '^package/services/metadata/core-properties/[^/]+\.psmdcp$'
    '^package/services/metadata/core-properties/[^/]+\.rels$'
    ('^lib/' + $escapedFramework + '/' + $escapedId + '\.dll$')
    ('^lib/' + $escapedFramework + '/' + $escapedId + '\.xml$')
)
$mainForbiddenPatterns = @(
    '(^|/)content/'
    '(^|/)contentFiles/'
    '(^|/)tools/'
    '\.cs$'
    '\.pdb$'
    '(^|/)snap7'
    '(^|/)test'
    '(^|/)obj/'
    '(^|/)bin/'
    '\.env$'
    '\.pfx$'
    '\.snk$'
    '\.key$'
    'secret'
)

$mainViolations = @(Get-ContentViolations -Entries $mainPackage.Entries -AllowedPatterns $mainAllowedPatterns -ForbiddenPatterns $mainForbiddenPatterns)
if ($mainViolations.Count -gt 0) {
    Exit-WithFailure -Code 'PACKAGE_CONTENT_INVALID' -Message "主包包含违规条目：$($mainViolations -join ', ')"
}

$packages = @()
$packages += [pscustomobject]@{
    Kind      = 'Main'
    FileName  = $actualMainFileName
    Version   = $mainVersion
    SizeBytes = [long](Get-Item -LiteralPath $packageFullPath).Length
    Entries   = @($mainPackage.Entries)
}

if ($effectiveSymbolMode -eq 'auto') {
    if (-not [string]::IsNullOrWhiteSpace($SymbolPackagePath)) {
        Exit-WithFailure -Code 'PACKAGE_CONTENT_INVALID' -Message 'auto 模式不允许提供符号包'
    }
} else {
    if ([string]::IsNullOrWhiteSpace($SymbolPackagePath) -or -not (Test-Path -LiteralPath $SymbolPackagePath)) {
        Exit-WithFailure -Code 'SYMBOL_PACK_FAILED' -Message 'manual 模式缺少符号包'
    }

    $symbolFullPath = (Resolve-Path -LiteralPath $SymbolPackagePath).Path
    $expectedSymbolFileName = "$ExpectedId.$ExpectedVersion.snupkg"
    $actualSymbolFileName = [IO.Path]::GetFileName($symbolFullPath)
    if ($actualSymbolFileName -ne $expectedSymbolFileName) {
        Exit-WithFailure -Code 'SYMBOL_PACK_FAILED' -Message "符号包文件名 $actualSymbolFileName 与预期 $expectedSymbolFileName 不一致"
    }

    $symbolPackage = Read-PackageEntries -PackageFullPath $symbolFullPath
    if ([string]::IsNullOrWhiteSpace($symbolPackage.NuspecText)) {
        Exit-WithFailure -Code 'SYMBOL_PACK_FAILED' -Message '符号包缺少 nuspec 文件'
    }

    [xml]$symbolNuspec = $symbolPackage.NuspecText
    $symbolMetadata = $symbolNuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $symbolMetadata) {
        Exit-WithFailure -Code 'SYMBOL_PACK_FAILED' -Message '符号包 nuspec 缺少 metadata 节点'
    }

    $symbolId = Get-NuspecValue -MetadataNode $symbolMetadata -Name 'id'
    $symbolVersion = Get-NuspecValue -MetadataNode $symbolMetadata -Name 'version'
    if ($symbolId -ne $ExpectedId -or $symbolVersion -ne $mainVersion) {
        Exit-WithFailure -Code 'SYMBOL_PACK_FAILED' -Message "符号包标识/版本不一致：$symbolId $symbolVersion（预期 $ExpectedId $mainVersion）"
    }

    $symbolAllowedPatterns = @(
        '^\[Content_Types\]\.xml$'
        '^_rels/\.rels$'
        ('^' + $escapedId + '\.nuspec$')
        '^package/services/metadata/core-properties/[^/]+\.psmdcp$'
        '^package/services/metadata/core-properties/[^/]+\.rels$'
        ('^lib/' + $escapedFramework + '/' + $escapedId + '\.pdb$')
    )
    $symbolForbiddenPatterns = @(
        '(^|/)content/'
        '(^|/)contentFiles/'
        '(^|/)tools/'
        '\.cs$'
        '(^|/)snap7'
        '(^|/)test'
        '(^|/)obj/'
        '(^|/)bin/'
        '\.env$'
        '\.pfx$'
        '\.snk$'
        '\.key$'
        'secret'
    )

    $symbolViolations = @(Get-ContentViolations -Entries $symbolPackage.Entries -AllowedPatterns $symbolAllowedPatterns -ForbiddenPatterns $symbolForbiddenPatterns)
    if ($symbolViolations.Count -gt 0) {
        Exit-WithFailure -Code 'PACKAGE_CONTENT_INVALID' -Message "符号包包含违规条目：$($symbolViolations -join ', ')"
    }

    $packages += [pscustomobject]@{
        Kind      = 'Symbol'
        FileName  = $actualSymbolFileName
        Version   = $symbolVersion
        SizeBytes = [long](Get-Item -LiteralPath $symbolFullPath).Length
        Entries   = @($symbolPackage.Entries)
    }
}

$state.Packages = $packages
Add-ReleaseStage -State $state -StageName 'PackageValidation'
Write-ReleaseState -State $state -StatePath $StatePath

foreach ($package in $packages) {
    Write-Output "[包校验] $($package.Kind)：$($package.FileName)（$($package.SizeBytes) bytes，$(@($package.Entries).Count) 个条目）"
}
Write-Output "[包校验] 通过：元数据、依赖声明与内容白名单校验完成（符号模式 $effectiveSymbolMode）"
exit 0