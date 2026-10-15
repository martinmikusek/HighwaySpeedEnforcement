<#
.SYNOPSIS
Lists acceptance scenarios (AC-xx in docs/domain/acceptance-scenarios.md) that no test covers through [Trait("AC", "AC-xx")].

.DESCRIPTION
Reports only; it never fails. Scenario ids are grouped by relevance: the ids given in -ProcessorScenarios are reported
under "processor", the rest under "generator / client". With no -ProcessorScenarios every id is listed in one group.

.EXAMPLE
pwsh tools/ac-coverage.ps1 -ProcessorScenarios AC-04,AC-05,AC-06,AC-07,AC-08,AC-09,AC-10
#>
[CmdletBinding()]
param(
    [string[]] $ProcessorScenarios = @(),
    [string] $ScenariosFile = (Join-Path $PSScriptRoot '..' 'docs' 'domain' 'acceptance-scenarios.md'),
    [string] $TestsDirectory = (Join-Path $PSScriptRoot '..' 'tests')
)

$ErrorActionPreference = 'Stop'

$scenarioIds = Get-Content -LiteralPath $ScenariosFile |
    ForEach-Object { if ($_ -match '^\|\s*(AC-\d+)\s*\|') { $Matches[1] } } |
    Sort-Object -Unique

$covered = Get-ChildItem -LiteralPath $TestsDirectory -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Select-String -Pattern 'Trait\(\s*"AC"\s*,\s*"(AC-\d+)"\s*\)' -AllMatches |
    ForEach-Object { $_.Matches | ForEach-Object { $_.Groups[1].Value } } |
    Sort-Object -Unique

$uncovered = $scenarioIds | Where-Object { $covered -notcontains $_ }
$processorSet = @($ProcessorScenarios | ForEach-Object { $_.Trim().ToUpperInvariant() })

Write-Host "Scenarios: $($scenarioIds.Count), covered by a test trait: $(@($scenarioIds | Where-Object { $covered -contains $_ }).Count), uncovered: $(@($uncovered).Count)"

if (@($uncovered).Count -eq 0) {
    Write-Host 'Every acceptance scenario has at least one test.'
    return
}

if ($processorSet.Count -eq 0) {
    Write-Host 'Uncovered:'
    $uncovered | ForEach-Object { Write-Host "  $_" }
    return
}

$groups = $uncovered | Group-Object { if ($processorSet -contains $_) { 'processor' } else { 'generator / client' } }

foreach ($group in $groups | Sort-Object Name -Descending) {
    Write-Host "Uncovered ($($group.Name)):"
    $group.Group | ForEach-Object { Write-Host "  $_" }
}
