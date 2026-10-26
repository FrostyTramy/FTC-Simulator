# Phase 10 acceptance: a full 2v2 MATCH plays start to finish (godot -- --match-test), then a fresh process re-simulates
# its replay and must reproduce it bit for bit (godot -- --verify-replay). Exit code 1 if either fails.
# Usage: ./scripts/test_match.ps1 [-NoBuild]
param([switch]$NoBuild)
. "$PSScriptRoot\godot_lib.ps1"

if (-not $NoBuild) { Build-GodotProject }
$out = [IO.Path]::Combine($RepoRoot, 'reports', 'match')
$code = Invoke-Godot @('--headless', '--fixed-fps', '240', '--path', $GodotProject, '--', '--match-test', $out) 1800
if ($code -ne 0) { Write-Host "match test failed (exit $code)"; exit 1 }
$summary = Get-Content ([IO.Path]::Combine($out, 'last_match.json')) -Raw | ConvertFrom-Json
$code = Invoke-Godot @('--headless', '--fixed-fps', '240', '--path', $GodotProject, '--', '--verify-replay', $summary.replay) 1800
if ($code -ne 0) { Write-Host "replay verification failed (exit $code)"; exit 1 }
Write-Host "match test passed: $($summary.summary), replay reproduces it"
exit 0
