<#
.SYNOPSIS
  本地构建、测试、校验、打包并发布 S7.NetCore 到 NuGet。

.DESCRIPTION
  步骤与 GitHub Actions 发布一致：Build -> Test(net10.0) -> Verify Output -> Pack -> Verify Package -> Push -> 结论。
  仅 net10.0 执行全量测试；四框架产物与包内四框架资产必须通过校验。构建与测试成功即可发布；
  不做版本格式/一致性校验、不预检、不重试、不补推。

.EXAMPLE
  $env:NUGET_API_KEY = '<your-api-key>'
  pwsh -File tools/publish/publish-nuget.ps1
  pwsh -File tools/publish/publish-nuget.ps1 -Version v5.0.0
  pwsh -File tools/publish/publish-nuget.ps1 -PackOnly
  pwsh -File tools/publish/publish-nuget.ps1 -SkipBuild -Version 5.0.0

.NOTES
  版本：-Version 优先；否则读取 Directory.Build.props 的 VersionPrefix，仅去掉 v/V 前缀。
  凭证：仅从环境变量 NUGET_API_KEY 读取；-PackOnly 不读密钥、不发起网络请求。
  重复版本：--skip-duplicate 使已存在版本跳过并成功，不覆盖。
  退出码：0 成功（含版本已存在、符号包未送达）；1 失败。
#>
[CmdletBinding()]
param([string]$Version, [switch]$SkipBuild, [switch]$PackOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PackageId = 'SiemensS7.NetCore'; $NuGetSource = 'https://api.nuget.org/v3/index.json'
$SolutionPath = 'S7.NetCore.sln'; $ProjectPath = 'src/S7.NetCore/S7.NetCore.csproj'; $PackagesDirectory = 'artifacts/packages'
$VerifyScript = 'tools/build/verify-frameworks.ps1'
$RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:failedStage = ''; $script:failReason = ''

function Assert-Environment {
    if ($env:OS -ne 'Windows_NT') { $script:failedStage = '环境'; $script:failReason = '需要 Windows + PowerShell 7.2+'; Write-Host "[环境] 失败：$($script:failReason)"; return $false }
    if ($PSVersionTable.PSVersion -lt [version]'7.2') { Write-Host "[环境] 警告：建议 PowerShell 7.2+，当前 $($PSVersionTable.PSVersion)" }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { $script:failedStage = '环境'; $script:failReason = '未找到 dotnet 命令'; Write-Host "[环境] 失败：$($script:failReason)"; return $false }
    Write-Host "[环境] Windows；PowerShell $($PSVersionTable.PSVersion)；dotnet $(& dotnet --version 2>&1 | Select-Object -First 1)"
    return $true
}

function Invoke-External {
    param([string]$FilePath, [string[]]$Arguments)
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $output = & $FilePath @Arguments 2>&1 | Out-String; $exitCode = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Resolve-Version {
    param([string]$RequestedVersion)
    $selected = ''; $source = ''
    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) { $selected = $RequestedVersion.Trim(); $source = 'Manual' }
    else {
        $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
        if (Test-Path -LiteralPath $propsPath) {
            [xml]$props = Get-Content -Raw -LiteralPath $propsPath
            $prefix = @($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) | Select-Object -First 1
            if (-not [string]::IsNullOrWhiteSpace($prefix)) { $selected = ([string]$prefix).Trim(); $source = 'VersionPrefix' }
        }
    }
    if ([string]::IsNullOrWhiteSpace($selected)) { $script:failedStage = '版本'; $script:failReason = '无法确定发布版本号'; Write-Host '[版本] 失败：无法确定发布版本号'; return $null }
    if ($selected -match '^[vV](?=\d)') { $selected = $selected.Substring(1) }
    Write-Host "[版本] 成功：$selected（来源 $source）"
    return [pscustomobject]@{ Version = $selected; Source = $source }
}

function Invoke-SolutionBuild {
    $result = Invoke-External -FilePath 'dotnet' -Arguments @('build', $SolutionPath, '-c', 'Release', '-p:ContinuousIntegrationBuild=true', '-p:DebugType=portable')
    Write-Host $result.Output
    if ($result.ExitCode -ne 0) { $script:failedStage = '构建'; $script:failReason = "dotnet build 退出码 $($result.ExitCode)"; Write-Host '[构建] 失败'; return $false }
    Write-Host '[构建] 成功：四框架全部通过'; return $true
}

function Invoke-Net10Tests {
    $result = Invoke-External -FilePath 'dotnet' -Arguments @('test', $SolutionPath, '-c', 'Release', '--no-build', '-f', 'net10.0')
    Write-Host $result.Output
    if ($result.ExitCode -ne 0) { $script:failedStage = '测试'; $script:failReason = "dotnet test 退出码 $($result.ExitCode)"; Write-Host '[测试] 失败'; return $false }
    $statistics = @($result.Output -split "`r?`n" |
        Where-Object { $_ -match '(Failed|失败)[:：]\s*\d+.*(Passed|通过)[:：]\s*\d+' } |
        Select-Object -Last 1)
    if ($statistics.Count -gt 0) { Write-Host "[测试] 成功：$($statistics[0].Trim())（框架 = net10.0）" }
    else { Write-Host '[测试] 成功（框架 = net10.0）' }
    return $true
}

function Invoke-Verification {
    param([string]$Mode, [string]$PackagePath)
    $scriptPath = Join-Path $RepositoryRoot $VerifyScript
    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        & $scriptPath -Mode $Mode -Configuration 'Release'
    } else {
        & $scriptPath -Mode $Mode -Configuration 'Release' -PackagePath $PackagePath
    }
    return $LASTEXITCODE
}

function Invoke-Pack {
    param([string]$PackageVersion)
    $outputDirectory = Join-Path $RepositoryRoot $PackagesDirectory
    $arguments = @('pack', $ProjectPath, '-c', 'Release', '--no-build', '-o', $outputDirectory, "-p:PackageVersion=$PackageVersion", '-p:IncludeSymbols=true', '-p:SymbolPackageFormat=snupkg')
    $result = Invoke-External -FilePath 'dotnet' -Arguments $arguments
    Write-Host $result.Output
    if ($result.ExitCode -ne 0) {
        $script:failedStage = '打包'; $script:failReason = "dotnet pack 退出码 $($result.ExitCode)"
        if ($SkipBuild) { Write-Host '[打包] 提示：已使用 -SkipBuild，若 Release 产物不存在，请去掉该开关重跑' }
        Write-Host '[打包] 失败'; return $null
    }
    $mainPackage = Join-Path $outputDirectory "$PackageId.$PackageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $mainPackage)) { $script:failedStage = '打包'; $script:failReason = '未产出主包文件'; Write-Host '[打包] 失败：未产出主包文件'; return $null }
    $symbolPackage = Join-Path $outputDirectory "$PackageId.$PackageVersion.snupkg"
    if (Test-Path -LiteralPath $symbolPackage) { Write-Host '[打包] 成功：主包与符号包已产出' } else { $symbolPackage = ''; Write-Host '[打包] 成功：仅主包（符号包未产出，不阻断发布）' }
    return [pscustomobject]@{ MainPackage = $mainPackage; SymbolPackage = $symbolPackage }
}

function Invoke-NuGetPush {
    param([string]$MainPackage)
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $pushOutput = & dotnet nuget push $MainPackage --api-key $env:NUGET_API_KEY --source $NuGetSource --skip-duplicate 2>&1 | Out-String; $exitCode = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    Write-Host $pushOutput

    $lines = @($pushOutput -split "`r?`n")
    $symbolIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match 'Pushing\s+\S+\.snupkg') { $symbolIndex = $i; break } }
    if ($symbolIndex -gt 0) { $mainLines = @($lines[0..($symbolIndex - 1)]); $symbolLines = @($lines[$symbolIndex..($lines.Count - 1)]) }
    elseif ($symbolIndex -eq 0) { $mainLines = @('error: main package output not detected'); $symbolLines = @($lines[0..($lines.Count - 1)]) }
    else { $mainLines = $lines; $symbolLines = @() }
    $mainFailed = @($mainLines | Where-Object { $_ -match 'error:' }).Count -gt 0
    $symbolFailed = @($symbolLines | Where-Object { $_ -match 'error:' }).Count -gt 0

    if ($exitCode -eq 0 -and -not $mainFailed) {
        $outcome = if ($pushOutput -match 'already exists|already been pushed|skipping') { 'AlreadyExists' } else { 'Pushed' }
        if ($symbolIndex -ge 0) {
            if ($symbolFailed) { $symbolStatus = 'Failed'; $outcome = 'SymbolWarned' } else { $symbolStatus = 'Pushed' }
        } else { $symbolStatus = 'NotSubmitted' }
    } elseif (-not $mainFailed -and $symbolIndex -gt 0 -and $symbolFailed) {
        $outcome = 'SymbolWarned'; $symbolStatus = 'Failed'; Write-Host '[推送] 主包已送达，符号包未送达（不阻断发布）'
    } else {
        $script:failedStage = '推送'; $script:failReason = '主包未确认送达'; Write-Host '[推送] 失败：主包未确认送达'
        return [pscustomobject]@{ Success = $false; Outcome = 'Failed'; SymbolStatus = 'Unknown' }
    }
    Write-Host "[推送] 成功：$outcome（符号包：$symbolStatus）"
    return [pscustomobject]@{ Success = $true; Outcome = $outcome; SymbolStatus = $symbolStatus }
}

function Write-RunSummary {
    param([string]$PackageVersion, [string]$VersionSource, [string]$MainPackage, [string]$SymbolPackage, [string]$PushOutcome, [string]$SymbolStatus)
    $mainFileName = if ($MainPackage) { [IO.Path]::GetFileName($MainPackage) } else { "$PackageId.$PackageVersion.nupkg" }
    $symbolFileName = if ($SymbolPackage) { [IO.Path]::GetFileName($SymbolPackage) } else { "$PackageId.$PackageVersion.snupkg" }
    $pushText = if ($PackOnly) { '未执行（-PackOnly）' } else { $PushOutcome }
    $symbolText = if ($PackOnly) { '未执行（-PackOnly）' } else { $SymbolStatus }
    Write-Host '===== 发布结论 ====='
    Write-Host "[结论] 运行结论：$pushText"
    Write-Host "[结论] 版本号：$PackageVersion（来源 $VersionSource）"
    Write-Host "[结论] 包标识：$PackageId"
    Write-Host "[结论] 主包：$mainFileName"
    Write-Host "[结论] 符号包：$symbolFileName（送达 $symbolText）"
    Write-Host "[结论] 推送源：$NuGetSource（结果 $pushText）"
    if ([string]::IsNullOrWhiteSpace($script:failedStage)) {
        Write-Host '[结论] 测试：net10.0 全量通过'
        Write-Host '[结论] 校验：四框架构建产物与包资产通过'
    }
    if (-not [string]::IsNullOrWhiteSpace($script:failedStage)) { Write-Host "[结论] 失败阶段：$($script:failedStage)"; Write-Host "[结论] 原因：$($script:failReason)" }
}

Push-Location $RepositoryRoot
try {
    Write-Host "[位置] 仓库根：$RepositoryRoot"
    $versionInfo = $null; $packInfo = $null; $pushResult = $null
    if (Assert-Environment) { $versionInfo = Resolve-Version -RequestedVersion $Version }
    if ($null -ne $versionInfo -and -not $PackOnly) {
        if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) { $script:failedStage = '凭证'; $script:failReason = '发布凭证缺失'; Write-Host '[凭证] 失败：发布凭证缺失' }
        else { Write-Host '[凭证] 已配置' }
    }
    if ($null -ne $versionInfo -and [string]::IsNullOrWhiteSpace($script:failedStage)) {
        if ($SkipBuild) { Write-Host '[构建] 已跳过（复用既有构建产物）' } else { $null = Invoke-SolutionBuild }
    }
    if ($null -ne $versionInfo -and [string]::IsNullOrWhiteSpace($script:failedStage)) { $null = Invoke-Net10Tests }
    if ($null -ne $versionInfo -and [string]::IsNullOrWhiteSpace($script:failedStage)) {
        $verifyExitCode = Invoke-Verification -Mode 'Output'
        if ($verifyExitCode -ne 0) { $script:failedStage = '产物校验'; $script:failReason = "verify-frameworks.ps1 退出码 $verifyExitCode"; Write-Host '[校验] 失败：构建产物' }
    }
    if ($null -ne $versionInfo -and [string]::IsNullOrWhiteSpace($script:failedStage)) { $packInfo = Invoke-Pack -PackageVersion $versionInfo.Version }
    if ($null -ne $packInfo -and [string]::IsNullOrWhiteSpace($script:failedStage)) {
        $verifyExitCode = Invoke-Verification -Mode 'Package' -PackagePath $packInfo.MainPackage
        if ($verifyExitCode -ne 0) { $script:failedStage = '包校验'; $script:failReason = "verify-frameworks.ps1 退出码 $verifyExitCode"; Write-Host '[校验] 失败：包资产' }
    }
    if ($null -ne $packInfo -and -not $PackOnly -and [string]::IsNullOrWhiteSpace($script:failedStage)) { $pushResult = Invoke-NuGetPush -MainPackage $packInfo.MainPackage }
} finally { Pop-Location }

$finalVersion = 'Unknown'; $finalSource = 'Unknown'; $finalMain = ''; $finalSymbol = ''; $finalOutcome = 'Failed'; $finalSymbolStatus = 'Unknown'
if ($null -ne $versionInfo) { $finalVersion = $versionInfo.Version; $finalSource = $versionInfo.Source }
if ($null -ne $packInfo) { $finalMain = $packInfo.MainPackage; $finalSymbol = $packInfo.SymbolPackage }
if ($PackOnly -and $null -ne $packInfo) { $finalOutcome = 'PackOnly' }
elseif ($null -ne $pushResult -and $pushResult.Success) { $finalOutcome = $pushResult.Outcome; $finalSymbolStatus = $pushResult.SymbolStatus }
elseif ($null -ne $pushResult) { $finalSymbolStatus = $pushResult.SymbolStatus }

Write-RunSummary -PackageVersion $finalVersion -VersionSource $finalSource -MainPackage $finalMain -SymbolPackage $finalSymbol -PushOutcome $finalOutcome -SymbolStatus $finalSymbolStatus
if (-not [string]::IsNullOrWhiteSpace($script:failedStage)) { exit 1 }
exit 0