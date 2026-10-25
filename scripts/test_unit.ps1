# Unit tests: Python (pytest), Java (JUnit; also builds the runner jars) and C# (xUnit, tests/dotnet).
. "$PSScriptRoot\lib\tools.ps1"

$failed = 0
$py = Find-Python
if (-not $py) { Write-Host 'python not found - run ./scripts/check_tools.ps1'; exit 1 }
Push-Location $RepoRoot
try {
    Write-Host '== Python (pytest)'
    & $py -m pytest -q -p no:cacheprovider
    if ($LASTEXITCODE -ne 0) { $failed++ }
} finally {
    Pop-Location
}

Write-Host '== Java (JUnit)'
& "$PSScriptRoot\test_java.ps1"
if ($LASTEXITCODE -ne 0) { $failed++ }

Write-Host '== C# (xUnit)'
& "$PSScriptRoot\test_dotnet.ps1"
if ($LASTEXITCODE -ne 0) { $failed++ }

if ($failed) { exit 1 }
exit 0
