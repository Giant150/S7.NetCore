[CmdletBinding()]
param(
    [string]$StatePath = 'artifacts/release/state.json',
    [AllowEmptyString()][string]$SummaryPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

function Format-PushOutcome {
    param($PushResult)

    if ($null -eq $PushResult) {
        return '未执行'
    }
    $attemptCount = @($PushResult.Attempts).Count
    if ($attemptCount -gt 0) {
        return "$($PushResult.Outcome)（尝试 $attemptCount 次）"
    }
    return [string]$PushResult.Outcome
}

function Format-Bytes {
    param([long]$Bytes)

    if ($Bytes -ge 1048576) {
        return ('{0:N2} MB' -f ($Bytes / 1048576.0))
    }
    if ($Bytes -ge 1024) {
        return ('{0:N2} KB' -f ($Bytes / 1024.0))
    }
    return "$Bytes B"
}

try {
    $state = Read-ReleaseState -StatePath $StatePath
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('## NuGet 发布运行摘要')
    $lines.Add('')

    $stateOutcome = ''
    if ($null -ne $state.PSObject.Properties['Outcome']) {
        $stateOutcome = [string]$state.Outcome
    }

    $versionText = '未解析'
    if ($null -ne $state.Version) {
        $versionText = "$($state.Version.Version)（标签 $($state.Version.TagText)）"
    }

    $symbolModeText = '未判定'
    $symbolEvidence = ''
    if ($null -ne $state.SymbolDecision) {
        $symbolModeText = [string]$state.SymbolDecision.Mode
        $symbolEvidence = [string]$state.SymbolDecision.Evidence
    }

    $testText = '未执行'
    $skippedReasonLines = @()
    if ($null -ne $state.Tests) {
        $testText = "总计 $($state.Tests.Total)，通过 $($state.Tests.Passed)，失败 $($state.Tests.Failed)，跳过 $($state.Tests.Skipped)"
        $skippedReasonLines = @($state.Tests.SkippedReasons)
    }

    $durationText = '未知'
    if (-not [string]::IsNullOrWhiteSpace([string]$state.StartedAtUtc)) {
        try {
            $startedAt = [datetime]::Parse([string]$state.StartedAtUtc, $null, [Globalization.DateTimeStyles]::RoundtripKind)
            $completedAt = [datetime]::UtcNow
            if (-not [string]::IsNullOrWhiteSpace([string]$state.CompletedAtUtc)) {
                $completedAt = [datetime]::Parse([string]$state.CompletedAtUtc, $null, [Globalization.DateTimeStyles]::RoundtripKind)
            }
            $seconds = [int]($completedAt - $startedAt).TotalSeconds
            if ($seconds -lt 0) {
                $seconds = 0
            }
            $durationText = "$seconds 秒"
        } catch {
            $durationText = '未知'
        }
    }

    $completedStages = '无'
    if ($null -ne $state.CompletedStages -and @($state.CompletedStages).Count -gt 0) {
        $completedStages = (@($state.CompletedStages) -join ' → ')
    }

    $lines.Add('| 字段 | 值 |')
    $lines.Add('|---|---|')
    $lines.Add("| 运行结论 | $($stateOutcome) |")
    $lines.Add("| 版本 | $versionText |")
    $lines.Add("| 包标识 | $($ReleasePolicyConstants.PackageId) |")
    $lines.Add("| 符号包推送方式 | $symbolModeText |")
    $lines.Add("| 符号包判定依据 | $symbolEvidence |")
    $lines.Add("| 主包推送结果 | $(Format-PushOutcome -PushResult $state.MainPush) |")
    $lines.Add("| 符号包推送结果 | $(Format-PushOutcome -PushResult $state.SymbolPush) |")
    $lines.Add("| 测试统计 | $testText |")
    $lines.Add("| 总耗时 | $durationText |")
    $lines.Add("| 已完成阶段 | $completedStages |")

    if ($null -ne $state.BuildDiagnostics) {
        $lines.Add("| 编译警告 | $($state.BuildDiagnostics.WarningCount) |")
    }

    if (@($state.Packages).Count -gt 0) {
        $lines.Add('')
        $lines.Add('### 包文件')
        $lines.Add('')
        $lines.Add('| 类型 | 文件名 | 版本 | 大小 |')
        $lines.Add('|---|---|---|---|')
        foreach ($package in @($state.Packages)) {
            $lines.Add("| $($package.Kind) | $($package.FileName) | $($package.Version) | $(Format-Bytes -Bytes ([long]$package.SizeBytes)) |")
        }
    }

    if ($skippedReasonLines.Count -gt 0) {
        $lines.Add('')
        $lines.Add('### 测试跳过原因')
        $lines.Add('')
        $lines.Add('| 原因 | 数量 |')
        $lines.Add('|---|---|')
        foreach ($reason in $skippedReasonLines) {
            $lines.Add("| $($reason.Reason) | $($reason.Count) |")
        }
    }

    if ($null -ne $state.Error) {
        $lines.Add('')
        $lines.Add('### 失败详情')
        $lines.Add('')
        $lines.Add("| 字段 | 值 |")
        $lines.Add('|---|---|')
        $lines.Add("| 错误码 | $($state.Error.Code) |")
        $lines.Add("| 失败阶段 | $($state.Error.Stage) |")
        $lines.Add("| 原因 | $($state.Error.Message) |")
        $lines.Add("| 处置建议 | $($state.Error.Remediation) |")
    }

    $summaryTarget = if ([string]::IsNullOrWhiteSpace($SummaryPath)) { $env:GITHUB_STEP_SUMMARY } else { $SummaryPath }
    $markdown = ($lines -join "`n") + "`n"

    if ([string]::IsNullOrWhiteSpace($summaryTarget)) {
        Write-Output $markdown
    } else {
        [IO.File]::AppendAllText($summaryTarget, $markdown, [Text.UTF8Encoding]::new($false))
        Write-Output '[摘要] 已写入运行摘要'
    }
} catch {
    Write-Warning "摘要生成失败：$($_.Exception.Message)"
}

exit 0