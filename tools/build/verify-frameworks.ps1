<#
.SYNOPSIS
  校验 S7.NetCore 的四个目标框架构建产物与 NuGet 包资产。

.DESCRIPTION
  只读校验：不编译、不还原、不联网、不调用 dotnet。
  Output 模式检查 src/S7.NetCore/bin/<Configuration> 下四个 TFM 的 dll 与 xml；
  Package 模式检查 nupkg 内四个 lib 资产、四个依赖组与包洁净度。

.PARAMETER Mode
  Output / Package / All。

.PARAMETER Configuration
  Debug 或 Release，默认 Release。

.PARAMETER PackagePath
  包路径，Package 或 All 模式必填；相对路径以仓库根为基准。

.PARAMETER RepositoryRoot
  仓库根目录，默认由脚本位置上溯两级。

.EXAMPLE
  pwsh -File tools/build/verify-frameworks.ps1 -Mode Output -Configuration Release

.EXAMPLE
  pwsh -File tools/build/verify-frameworks.ps1 -Mode Package -PackagePath artifacts/packages/SiemensS7.NetCore.5.1.0.nupkg

.NOTES
  退出码：0 通过；1 校验失败；2 参数或配置非法。
#>
[CmdletBinding()]
param(
    [string]$Mode = 'Output',
    [string]$Configuration = 'Release',
    [string]$PackagePath,
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Mode -notin @('Output', 'Package', 'All')) {
    Write-Host "[参数] 失败：-Mode 必须是 Output、Package 或 All，实际为 '$Mode'"
    exit 2
}
if ($Configuration -notin @('Debug', 'Release')) {
    Write-Host "[参数] 失败：-Configuration 必须是 Debug 或 Release，实际为 '$Configuration'"
    exit 2
}
if ($Mode -in @('Package', 'All') -and [string]::IsNullOrWhiteSpace($PackagePath)) {
    Write-Host '[参数] 失败：Package 或 All 模式必须提供 -PackagePath'
    exit 2
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

$propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $propsPath)) {
    Write-Host "[配置] 失败：找不到 $propsPath"
    exit 2
}

[xml]$props = Get-Content -Raw -LiteralPath $propsPath
$targetFrameworks = @($props.Project.PropertyGroup |
    ForEach-Object { $_.S7TargetFrameworks } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1)
$testTargetFramework = @($props.Project.PropertyGroup |
    ForEach-Object { $_.S7TestTargetFramework } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1)

$frameworks = @()
if ($targetFrameworks.Count -eq 1) {
    $frameworks = @($targetFrameworks[0] -split ';' |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}
if ($frameworks.Count -eq 0 -or $testTargetFramework.Count -ne 1) {
    Write-Host '[配置] 失败：Directory.Build.props 缺少 S7TargetFrameworks 或 S7TestTargetFramework'
    exit 2
}
$testFramework = [string]$testTargetFramework[0]
if ($testFramework -notin $frameworks) {
    Write-Host "[配置] 失败：S7TestTargetFramework '$testFramework' 不在 S7TargetFrameworks 中"
    exit 2
}

function Get-VerificationMode {
    param([string]$TargetFramework)
    if ($TargetFramework -eq $testFramework) { return '完整测试验证' }
    return '仅构建验证'
}

function Test-OutputArtifacts {
    $failed = $false
    foreach ($tfm in $frameworks) {
        $tfmDirectory = Join-Path $RepositoryRoot "src/S7.NetCore/bin/$Configuration/$tfm"
        $dllPath = Join-Path $tfmDirectory 'S7.NetCore.dll'
        $xmlPath = Join-Path $tfmDirectory 'S7.NetCore.xml'
        $missing = @()
        if (-not (Test-Path -LiteralPath $dllPath)) { $missing += $dllPath }
        if (-not (Test-Path -LiteralPath $xmlPath)) { $missing += $xmlPath }
        if ($missing.Count -eq 0) {
            Write-Host "[TFM] $tfm BUILD=PASS MODE=$(Get-VerificationMode -TargetFramework $tfm)"
        } else {
            $failed = $true
            foreach ($path in $missing) { Write-Host "[TFM] $tfm MISSING: $path" }
        }
    }
    return -not $failed
}

function Get-FrameworkKey {
    param([string]$TargetFrameworkValue)
    if ([string]::IsNullOrWhiteSpace($TargetFrameworkValue)) { return '' }
    if ($TargetFrameworkValue -match '^\.NETCoreApp(?<version>\d+\.\d+)$') {
        return "net$($Matches.version)"
    }
    return $TargetFrameworkValue.Trim().ToLowerInvariant()
}

function Test-PackageArtifacts {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        Write-Host '[包] 失败：未提供包路径'
        return $false
    }
    $packageFile = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $RepositoryRoot $Path }
    $packageFile = [IO.Path]::GetFullPath($packageFile)
    if (-not (Test-Path -LiteralPath $packageFile)) {
        Write-Host "[包] 失败：找不到包 $packageFile"
        return $false
    }

    if (-not ('System.IO.Compression.ZipFile' -as [type])) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }

    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("s7-verify-" + [Guid]::NewGuid().ToString('N'))
    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($packageFile, $temporaryDirectory)
        $failed = $false

        $libDirectory = Join-Path $temporaryDirectory 'lib'
        if (-not (Test-Path -LiteralPath $libDirectory)) {
            Write-Host '[包] 失败：包内缺少 lib 目录'
            return $false
        }

        $libFrameworks = @(Get-ChildItem -LiteralPath $libDirectory -Directory | ForEach-Object { $_.Name })
        foreach ($libFramework in $libFrameworks) {
            if ($libFramework -notin $frameworks) {
                $failed = $true
                Write-Host "[包] 失败：存在非目标框架资产目录 lib/$libFramework"
            }
        }

        foreach ($tfm in $frameworks) {
            $tfmDirectory = Join-Path $libDirectory $tfm
            $dllPath = Join-Path $tfmDirectory 'S7.NetCore.dll'
            $xmlPath = Join-Path $tfmDirectory 'S7.NetCore.xml'
            $missing = @()
            if (-not (Test-Path -LiteralPath $dllPath)) { $missing += "lib/$tfm/S7.NetCore.dll" }
            if (-not (Test-Path -LiteralPath $xmlPath)) { $missing += "lib/$tfm/S7.NetCore.xml" }
            if ($missing.Count -eq 0) {
                Write-Host "[TFM] $tfm PACKAGE=PASS MODE=$(Get-VerificationMode -TargetFramework $tfm)"
            } else {
                $failed = $true
                foreach ($entry in $missing) { Write-Host "[包] 失败：缺少资产 $entry" }
            }
        }

        $nuspecFile = Get-ChildItem -LiteralPath $temporaryDirectory -Filter *.nuspec -File | Select-Object -First 1
        if ($null -eq $nuspecFile) {
            Write-Host '[包] 失败：包内缺少 nuspec'
            return $false
        }
        [xml]$nuspec = Get-Content -Raw -LiteralPath $nuspecFile.FullName
        $groups = @($nuspec.package.metadata.dependencies.group)
        if ($groups.Count -ne $frameworks.Count) {
            $failed = $true
            Write-Host "[包] 失败：依赖组数量为 $($groups.Count)，期望 $($frameworks.Count)"
        }

        foreach ($tfm in $frameworks) {
            $group = @($groups | Where-Object { (Get-FrameworkKey -TargetFrameworkValue ([string]$_.targetFramework)) -eq $tfm })
            if ($group.Count -ne 1) {
                $failed = $true
                Write-Host "[包] 失败：$tfm 依赖组数量为 $($group.Count)，期望 1"
                continue
            }
            $dependencyNodes = @($group[0].ChildNodes | Where-Object { $_.LocalName -eq 'dependency' })
            $loggingDependencies = @($dependencyNodes | Where-Object { $_.id -eq 'Microsoft.Extensions.Logging.Abstractions' })
            if ($loggingDependencies.Count -eq 0 -and $tfm -eq 'net11.0') {
                Write-Host '[依赖] net11.0 Microsoft.Extensions.Logging.Abstractions 由 .NET 11 共享框架提供，无需显式依赖 PASS'
                continue
            }
            if ($loggingDependencies.Count -ne 1) {
                $failed = $true
                Write-Host "[包] 失败：$tfm 缺少 Microsoft.Extensions.Logging.Abstractions 依赖"
                continue
            }
            $dependencyVersion = [string]$loggingDependencies[0].version
            $expectedMajor = [int]($tfm -replace '^net(\d+)\.0$', '$1')
            if ($dependencyVersion -match '-') {
                $failed = $true
                Write-Host "[包] 失败：$tfm 依赖版本 $dependencyVersion 含预发布后缀"
            } elseif (-not $dependencyVersion.StartsWith("$expectedMajor.")) {
                $failed = $true
                Write-Host "[包] 失败：$tfm 依赖版本 $dependencyVersion 与主版本 $expectedMajor 不一致"
            } else {
                Write-Host "[依赖] $tfm Microsoft.Extensions.Logging.Abstractions=$dependencyVersion PASS"
            }
        }

        $packageFiles = @(Get-ChildItem -LiteralPath $temporaryDirectory -Recurse -File)
        foreach ($file in $packageFiles) {
            $relativePath = $file.FullName.Substring($temporaryDirectory.Length).TrimStart('\', '/') -replace '\\', '/'
            $violation = ''
            if ($file.Extension -in @('.cs', '.csproj')) { $violation = '测试工程或源码文件' }
            elseif ($file.Extension -eq '.trx') { $violation = '测试结果文件' }
            elseif ($file.Name -in @('snap7.dll', 'libsnap7.so')) { $violation = '原生库' }
            elseif ($relativePath -match '(^|/)(bin|obj)/') { $violation = 'bin/obj 中间产物' }
            if ($violation -ne '') {
                $failed = $true
                Write-Host "[包] 失败：$relativePath（$violation）"
            }
        }

        if (-not $failed) {
            Write-Host "[包] 成功：四框架资产、四依赖组与洁净度校验通过（$packageFile）"
        }
        return -not $failed
    } finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$success = $true
if ($Mode -in @('Output', 'All')) {
    $success = Test-OutputArtifacts
}
if ($success -and $Mode -in @('Package', 'All')) {
    $success = Test-PackageArtifacts -Path $PackagePath
}

if (-not $success) {
    exit 1
}
Write-Host '[校验] 通过'
exit 0