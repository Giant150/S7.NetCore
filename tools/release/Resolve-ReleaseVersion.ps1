[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EventName,
    [AllowEmptyString()][string]$EventAction = '',
    [AllowEmptyString()][string]$Tag = '',
    [AllowEmptyString()][string]$IsPrerelease = 'false',
    [AllowEmptyString()][string]$IsDraft = 'false',
    [string]$RepositoryRoot = '.',
    [string]$StatePath = 'artifacts/release/state.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ReleasePolicy.psm1') -Force

function Add-ActionOutput {
    param([string]$Name, [string]$Value)
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        return
    }
    $line = "$Name=$Value" + "`n"
    [IO.File]::AppendAllText($env:GITHUB_OUTPUT, $line, [Text.UTF8Encoding]::new($false))
}

function Complete-SkippedRun {
    param([string]$Reason)

    $state = Read-ReleaseState -StatePath $script:statePath
    $state.Request = [pscustomobject]@{
        TriggerKind = $script:triggerKind
        TagText     = $Tag
        IsPrerelease = $script:prereleaseFlag
        IsDraft     = $script:draftFlag
    }
    $state | Add-Member -NotePropertyName 'Outcome' -NotePropertyValue 'skipped' -Force
    $state | Add-Member -NotePropertyName 'SkipReason' -NotePropertyValue 'EVENT_SKIPPED' -Force
    $state.CompletedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    Add-ReleaseStage -State $state -StageName 'Trigger'
    Write-ReleaseState -State $state -StatePath $script:statePath

    Add-ActionOutput -Name 'outcome' -Value 'skipped'
    Add-ActionOutput -Name 'version' -Value ''
    Add-ActionOutput -Name 'tag' -Value $Tag

    Write-Output "[版本校验] 跳过：$Reason"
    exit 0
}

function Exit-WithFailure {
    param([string]$Code, [string]$Message)

    $errorRecord = Write-ReleaseFailure -StatePath $script:statePath -Code $Code -Message $Message
    Write-Output "[版本校验] 失败：$($errorRecord.Code) - $Message"
    Write-Output "[版本校验] 处置建议：$($errorRecord.Remediation)"
    $info = Get-ReleaseErrorInfo -Code $Code
    exit $info.ExitCode
}

$repositoryRootResolved = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$script:statePath = if ([IO.Path]::IsPathRooted($StatePath)) {
    $StatePath
} else {
    Join-Path $repositoryRootResolved $StatePath
}

$script:prereleaseFlag = ($IsPrerelease -eq 'true')
$script:draftFlag = ($IsDraft -eq 'true')
$script:triggerKind = if ($EventName -eq 'workflow_dispatch') { 'ManualDispatch' } else { 'ReleasePublished' }

$isReleaseEvent = ($EventName -eq 'release')
$isDispatchEvent = ($EventName -eq 'workflow_dispatch')
$releasePublished = $isReleaseEvent -and ([string]::IsNullOrWhiteSpace($EventAction) -or $EventAction -eq 'published') -and -not $script:draftFlag
$manualDispatch = $isDispatchEvent -and -not [string]::IsNullOrWhiteSpace($Tag)

if (-not ($releasePublished -or $manualDispatch)) {
    Complete-SkippedRun -Reason '当前事件不是 release.published，且未提供有效的手动标签'
}

if ($script:prereleaseFlag -or $Tag.Contains('-')) {
    Exit-WithFailure -Code 'PRERELEASE_REJECTED' -Message "拒绝预发布版本：$Tag"
}

$parsed = ConvertFrom-ReleaseTag -Tag $Tag
if (-not $parsed.Success) {
    Exit-WithFailure -Code 'TAG_FORMAT_INVALID' -Message $parsed.Reason
}

$versionPrefix = Get-VersionPrefix -RepositoryRoot $repositoryRootResolved
$consistency = Assert-VersionConsistency -Version $parsed.Version -VersionPrefix $versionPrefix
if (-not $consistency.Consistent) {
    Exit-WithFailure -Code 'VERSION_MISMATCH' -Message "标签版本 $($consistency.Version) 与 VersionPrefix $($consistency.VersionPrefix) 不一致"
}

$state = Read-ReleaseState -StatePath $script:statePath
$state.Request = [pscustomobject]@{
    TriggerKind  = $script:triggerKind
    TagText      = $Tag.Trim()
    IsPrerelease = $script:prereleaseFlag
    IsDraft      = $script:draftFlag
}
$state.Version = [pscustomobject]@{
    TagText       = $Tag.Trim()
    Version       = $consistency.Version
    VersionPrefix = $consistency.VersionPrefix
    IsFormal      = $true
}
$state | Add-Member -NotePropertyName 'Outcome' -NotePropertyValue 'proceed' -Force
Add-ReleaseStage -State $state -StageName 'VersionValidation'
Write-ReleaseState -State $state -StatePath $script:statePath

Add-ActionOutput -Name 'outcome' -Value 'proceed'
Add-ActionOutput -Name 'version' -Value $consistency.Version
Add-ActionOutput -Name 'tag' -Value $Tag.Trim()

Write-Output "[版本校验] 通过：版本 $($consistency.Version)（标签 $($Tag.Trim())，触发器 $($script:triggerKind)）"
exit 0