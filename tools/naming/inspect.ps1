$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail)
    $script:checks++
    if ($Ok) {
        Write-Output "$Name PASS"
    } else {
        $failures.Add("$Name FAIL: $Detail")
        Write-Output "$Name FAIL: $Detail"
    }
}

$legacyDirs = @(
    (Join-Path $root 'src/Snap7.NetCore'),
    (Join-Path $root 'tests/Snap7.NetCore.Tests')
)
Check 'N-01' (-not ($legacyDirs | Where-Object { Test-Path $_ })) '旧目录仍存在'

$legacyFiles = @(
    (Join-Path $root 'Snap7.NetCore.sln'),
    (Join-Path $root 'src/S7.NetCore/Snap7.NetCore.csproj'),
    (Join-Path $root 'tests/S7.NetCore.Tests/Snap7.NetCore.Tests.csproj')
)
Check 'N-02' (-not ($legacyFiles | Where-Object { Test-Path $_ })) '旧项目/解决方案文件仍存在'

$solutionPath = Join-Path $root 'S7.NetCore.sln'
$solution = Get-Content -Raw -LiteralPath $solutionPath
Check 'N-03' ($solution.Contains('src\S7.NetCore\S7.NetCore.csproj') -and $solution.Contains('tests\S7.NetCore.Tests\S7.NetCore.Tests.csproj')) '解决方案项目路径不正确'
Check 'N-03b' (-not $solution.Contains('Snap7.NetCore')) '解决方案仍含旧名'

$mainProject = Get-Content -Raw -LiteralPath (Join-Path $root 'src/S7.NetCore/S7.NetCore.csproj')
Check 'N-04' ($mainProject.Contains('<RootNamespace>S7.NetCore</RootNamespace>') -and $mainProject.Contains('<AssemblyName>S7.NetCore</AssemblyName>') -and $mainProject.Contains('<InternalsVisibleTo Include="S7.NetCore.Tests" />')) '主项目属性不正确'

$testProject = Get-Content -Raw -LiteralPath (Join-Path $root 'tests/S7.NetCore.Tests/S7.NetCore.Tests.csproj')
Check 'N-05' ($testProject.Contains('<AssemblyName>S7.NetCore.Tests</AssemblyName>') -and $testProject.Contains('<RootNamespace>S7.NetCore.Tests</RootNamespace>') -and $testProject.Contains('src\S7.NetCore\S7.NetCore.csproj')) '测试项目属性或引用不正确'

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $root 'src/S7.NetCore'), (Join-Path $root 'tests/S7.NetCore.Tests') -Recurse -Filter *.cs |
    Where-Object { $_.Name -ne 'snap7.net.cs' -and $_.FullName -notmatch 'bin|obj' -and $_.FullName -notmatch 'Naming' }
$legacyHits = @($sourceFiles | Select-String -Pattern 'Snap7\.NetCore' -CaseSensitive)
Check 'N-06/N-07/N-13' ($legacyHits.Count -eq 0) ("源码旧名命中 $($legacyHits.Count) 处")

$pathTest = Get-Content -Raw -LiteralPath (Join-Path $root 'tests/S7.NetCore.Tests/VirtualPlc/TypedDb1ReadOnlyTests.cs')
Check 'N-08' ($pathTest.Contains('tests/S7.NetCore.Tests/VirtualPlc')) '测试路径字面量未更新'

$readme = [IO.File]::ReadAllText((Join-Path $root 'README.md'), [Text.UTF8Encoding]::new($false))
Check 'N-11' ($readme.Contains('dotnet build S7.NetCore.sln') -and $readme.Contains('dotnet test S7.NetCore.sln') -and -not $readme.Contains('dotnet build Snap7.NetCore.sln')) 'README 命令未同步'
$migrationIndex = $readme.IndexOf('## 变更记录与迁移')
Check 'N-13b' ($migrationIndex -gt 0 -and -not $readme.Substring(0, $migrationIndex).Contains('Snap7.NetCore')) 'README 白名单外存在旧名'

$props = Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props')
Check 'N-15' ($props.Contains('<VersionPrefix>2.1.0</VersionPrefix>')) '版本声明缺失或不是 2.1.0'

$outputDir = Join-Path $root 'src/S7.NetCore/bin/Debug/net10.0'
Check 'N-12' ((Test-Path (Join-Path $outputDir 'S7.NetCore.dll')) -or -not (Test-Path $outputDir)) 'Debug 输出缺少 S7.NetCore.dll'
Check 'N-14' (-not (Test-Path (Join-Path $outputDir 'Snap7.NetCore.dll'))) 'Debug 输出存在旧程序集'

$typeForwardedHits = @($sourceFiles | Select-String -Pattern 'TypeForwardedTo' -CaseSensitive)
Check 'N-14b' ($typeForwardedHits.Count -eq 0) '存在类型转发'

$nativeProjectPath = Join-Path $root 'tests/S7.NetCore.Tests.Native/S7.NetCore.Tests.Native.csproj'
$nativeProject = Get-Content -Raw -LiteralPath $nativeProjectPath
Check 'N-05b' ($testProject.Contains('S7.NetCore.Tests.Native') -and $nativeProject.Contains('snap7.net.cs')) '原生桥接引用不完整'

$snap7Source = Join-Path $root 'tests/S7.NetCore.Tests/snap7.net.cs'
$expectedHash = 'F0C917F51A8938A1A9A752B6821CF11857214479A4542901C9225A5E46765B16'
$actualHash = (Get-FileHash -LiteralPath $snap7Source -Algorithm SHA256).Hash
Check 'N-16' ($actualHash -eq $expectedHash) "原生桥接哈希变化：$actualHash"

$prodHits = @(Get-ChildItem -LiteralPath (Join-Path $root 'src/S7.NetCore') -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch 'bin|obj' } |
    Select-String -Pattern 'DllImport|LibraryImport|NativeLibrary')
Check 'N-17' ($prodHits.Count -eq 0) '生产代码含原生互操作'

$nativeHits = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests/S7.NetCore.Tests') -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch 'bin|obj|Naming|Server' -and $_.Name -ne 'snap7.net.cs' } |
    Select-String -Pattern 'using Snap7;|Snap7.S7Server')
Check 'N-19' ($nativeHits.Count -eq 0) '原生类型泄漏到普通测试'

$nativeOutput = Join-Path $root 'tests/S7.NetCore.Tests/bin/Debug/net10.0/snap7.dll'
Check 'N-18' ((Test-Path $nativeOutput) -and $nativeProject.Contains('snap7.net.cs')) '测试侧原生能力不完整'

$endpointText = [IO.File]::ReadAllText(
    (Join-Path $root 'tests/S7.NetCore.Tests/VirtualPlc/VirtualPlcEndpoint.cs'),
    [Text.UTF8Encoding]::new($false))
Check 'N-20' ($endpointText.Contains('1102')) '虚拟 PLC 默认端点不是 1102'

Write-Output ""
if ($failures.Count -gt 0) {
    Write-Output "FAIL: 检查项 $checks，失败 $($failures.Count)"
    exit 1
}

Write-Output "PASS: 检查项 $checks，失败 0；白名单：.codeartsdoer/specs、README 迁移段、snap7.net.cs、snap7.dll"