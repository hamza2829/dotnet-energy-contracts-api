param([string]$ResultsDirectory = 'TestResults')

$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem -Path $ResultsDirectory -Filter *.trx -Recurse -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    throw 'No TRX files were produced. Check restore, build, and test logs.'
}

$lines = @('## Test results', '', '| Suite | Total | Passed | Failed | Skipped |', '| --- | ---: | ---: | ---: | ---: |')
$total = 0
$passed = 0
$failed = 0
$skipped = 0
foreach ($file in $files) {
    [xml]$report = Get-Content -LiteralPath $file.FullName -Raw
    $counters = $report.TestRun.ResultSummary.Counters
    $suite = $file.Directory.Name
    $suiteSkipped = [int]$counters.total - [int]$counters.passed - [int]$counters.failed
    $lines += "| $suite | $($counters.total) | $($counters.passed) | $($counters.failed) | $suiteSkipped |"
    $total += [int]$counters.total
    $passed += [int]$counters.passed
    $failed += [int]$counters.failed
    $skipped += $suiteSkipped
}
$lines += "| **Total** | **$total** | **$passed** | **$failed** | **$skipped** |"
$lines -join "`n" | Write-Output
if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
}
if ($total -eq 0) { throw 'The test runner discovered zero tests.' }
