[CmdletBinding()]
param(
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$StatePath = 'artifacts/release/state.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

function Get-SkipReason {
    param($ResultNode, $RunInfoNodes)

    $reason = ''
    $messageNode = $ResultNode.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
    if ($null -ne $messageNode -and -not [string]::IsNullOrWhiteSpace($messageNode.InnerText)) {
        $reason = $messageNode.InnerText
    }

    if ([string]::IsNullOrWhiteSpace($reason)) {
        $stdoutNode = $ResultNode.SelectSingleNode("./*[local-name()='Output']/*[local-name()='StdOut']")
        if ($null -ne $stdoutNode -and -not [string]::IsNullOrWhiteSpace($stdoutNode.InnerText)) {
            $reason = $stdoutNode.InnerText
        }
    }

    if ([string]::IsNullOrWhiteSpace($reason)) {
        foreach ($info in $RunInfoNodes) {
            $textNode = $info.SelectSingleNode("./*[local-name()='Text']")
            if ($null -ne $textNode -and -not [string]::IsNullOrWhiteSpace($textNode.InnerText)) {
                $reason = $textNode.InnerText
                break
            }
            $infoMessageNode = $info.SelectSingleNode("./*[local-name()='Message']")
            if ($null -ne $infoMessageNode -and -not [string]::IsNullOrWhiteSpace($infoMessageNode.InnerText)) {
                $reason = $infoMessageNode.InnerText
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($reason)) {
        $reason = '未提供跳过原因'
    }

    $firstLine = ($reason -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    $normalized = $firstLine.Trim()
    if ($normalized.Length -gt 200) {
        $normalized = $normalized.Substring(0, 200)
    }
    return $normalized
}

$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -ErrorAction SilentlyContinue)
if ($trxFiles.Count -eq 0) {
    $errorRecord = Write-ReleaseFailure -StatePath $StatePath -Code 'TEST_FAILED' -Message '无测试结果：未找到任何 TRX 文件'
    Write-Output '[测试门禁] 失败：无测试结果'
    Write-Output "[测试门禁] 处置建议：$($errorRecord.Remediation)"
    exit (Get-ReleaseErrorInfo -Code 'TEST_FAILED').ExitCode
}

$total = 0
$passed = 0
$failed = 0
$skipped = 0
$failedTests = @()
$skipReasonCounts = @{}

foreach ($trxFile in $trxFiles) {
    [xml]$document = [IO.File]::ReadAllText($trxFile.FullName, [Text.UTF8Encoding]::new($false))
    $runInfoNodes = @($document.SelectNodes("//*[local-name()='ResultSummary']/*[local-name()='RunInfos']/*[local-name()='RunInfo']"))
    $resultNodes = @($document.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']"))

    foreach ($resultNode in $resultNodes) {
        $total++
        $outcome = $resultNode.GetAttribute('outcome')
        $testName = $resultNode.GetAttribute('testName')

        if ($outcome -eq 'Passed') {
            $passed++
        } elseif ($outcome -eq 'Failed') {
            $failed++
            $failedTests += $testName
        } else {
            $skipped++
            $reason = Get-SkipReason -ResultNode $resultNode -RunInfoNodes $runInfoNodes
            if ($skipReasonCounts.ContainsKey($reason)) {
                $skipReasonCounts[$reason] = $skipReasonCounts[$reason] + 1
            } else {
                $skipReasonCounts[$reason] = 1
            }
        }
    }
}

$skippedReasons = @($skipReasonCounts.GetEnumerator() |
    Sort-Object -Property Value -Descending |
    ForEach-Object { [pscustomobject]@{ Reason = $_.Key; Count = $_.Value } })

$state = Read-ReleaseState -StatePath $StatePath
$state.Tests = [pscustomobject]@{
    Total          = $total
    Passed         = $passed
    Failed         = $failed
    Skipped        = $skipped
    FailedTests    = @($failedTests)
    SkippedReasons = @($skippedReasons)
}
Add-ReleaseStage -State $state -StageName 'Test'
Write-ReleaseState -State $state -StatePath $StatePath

foreach ($reason in $skippedReasons) {
    Write-Output "[测试门禁] 跳过原因：$($reason.Reason) x $($reason.Count)"
}

if ($failed -gt 0) {
    $errorRecord = Write-ReleaseFailure -StatePath $StatePath -Code 'TEST_FAILED' -Message "失败用例 $failed 个：$($failedTests -join ', ')"
    Write-Output "[测试门禁] 失败：$($failedTests -join ', ')"
    Write-Output "[测试门禁] 处置建议：$($errorRecord.Remediation)"
    exit (Get-ReleaseErrorInfo -Code 'TEST_FAILED').ExitCode
}

Write-Output "[测试门禁] 通过：总计 $total，通过 $passed，失败 $failed，跳过 $skipped"
exit 0