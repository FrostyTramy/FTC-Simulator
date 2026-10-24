# Runs every test suite that exists so far and the resource inventory check. Exit code 1 on any failure.
. "$PSScriptRoot\lib\tools.ps1"

$failures = 0

Write-Host '== Resource inventory (originals match tools/inventory/resources_manifest.json)'
& (Find-Python) -I "$RepoRoot\tools\inventory\inventory.py"
if ($LASTEXITCODE -ne 0) { $failures++ }

Write-Host '== Unit tests'
& "$PSScriptRoot\test_unit.ps1"
if ($LASTEXITCODE -ne 0) { $failures++ }

if ($failures) { Write-Host "FAILED: $failures suite(s)"; exit 1 }
Write-Host 'ALL PASSED'
exit 0
