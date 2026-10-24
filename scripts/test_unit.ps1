# Unit tests. Phase 0: Python (pytest). C# and Java unit tests are added here when those projects exist.
. "$PSScriptRoot\lib\tools.ps1"

$py = Find-Python
if (-not $py) { Write-Host 'python not found - run ./scripts/check_tools.ps1'; exit 1 }
Push-Location $RepoRoot
try {
    & $py -m pytest -q -p no:cacheprovider
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
