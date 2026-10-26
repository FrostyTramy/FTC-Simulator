# Runs every C# test project under tests/dotnet (xUnit). Exit code 1 if any project fails.
. "$PSScriptRoot\lib\tools.ps1"

Build-JavaJars   # the runner tests start robot code on the SDK shim and runner jars
$failed = 0
$projects = Get-ChildItem -Path (Join-Path $RepoRoot 'tests\dotnet') -Filter '*.csproj' -Recurse | Sort-Object FullName
foreach ($project in $projects) {
    Write-Host "== $($project.BaseName)"
    & dotnet test $project.FullName -nologo -v q
    if ($LASTEXITCODE -ne 0) { $failed++ }
}
if ($failed) { Write-Host "FAILED: $failed C# test project(s)"; exit 1 }
exit 0
