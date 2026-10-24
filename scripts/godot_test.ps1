# Headless scenario tests (tests/scenarios/*.yaml). Exit code 1 if any scenario fails.
# Usage: ./scripts/godot_test.ps1 [-Filter field_settles] [-NoBuild]
# Physics runs faster than real time: --fixed-fps 240 = one 240 Hz physics step per frame.
param([string]$Filter = '', [switch]$NoBuild)
. "$PSScriptRoot\godot_lib.ps1"

if (-not $NoBuild) { Build-GodotProject }
$godotArgs = @('--headless', '--fixed-fps', '240', '--path', $GodotProject, '--', '--run-scenarios')
if ($Filter) { $godotArgs += $Filter }
$code = Invoke-Godot $godotArgs 900
Write-Host "scenario tests exit code: $code (results: reports/scenarios/results.json)"
exit $code
