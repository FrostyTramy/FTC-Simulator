# Java (JUnit): builds the SDK shim, the OpMode runner and the example TeamCode (build adapter) and runs their tests.
# The C# runner tests (tests/dotnet/Sim.Runner.Tests) start the runner from the jars built here.
. "$PSScriptRoot\lib\tools.ps1"

$jdk = Find-Jdk17
if (-not $jdk) { Write-Host 'JDK 17 not found - run ./scripts/check_tools.ps1'; exit 1 }
$env:JAVA_HOME = $jdk

$gradle = Find-Gradle

Push-Location (Join-Path $RepoRoot 'java')
try {
    & $gradle --console=plain -q test assemble
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAILED: Java build or tests'; exit 1 }
} finally {
    Pop-Location
}
exit 0
