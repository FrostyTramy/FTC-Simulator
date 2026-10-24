# Shared helpers for the godot_*.ps1 scripts. Dot-source: . "$PSScriptRoot\godot_lib.ps1"
. "$PSScriptRoot\lib\tools.ps1"

$GodotProject = Join-Path $RepoRoot 'godot'

function Get-GodotExe {
    $exe = Find-Godot
    if (-not $exe) { throw 'Godot .NET not found - see docs/SETUP.md (or set GODOT_BIN)' }
    return $exe
}

function Invoke-Godot([string[]]$GodotArgs, [int]$TimeoutSeconds = 600) {
    # Runs the Godot console exe with output in this console; returns its exit code (124 on timeout).
    $exe = Get-GodotExe
    $p = Start-Process -FilePath $exe -ArgumentList $GodotArgs -NoNewWindow -PassThru
    $null = $p.Handle  # keeps the exit code available after the process ends
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Get-Process | Where-Object { $_.ProcessName -like 'Godot_v4*' } | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "Godot did not finish within $TimeoutSeconds s"
        return 124
    }
    return $p.ExitCode
}

function Build-GodotProject {
    # Compiles the C# project and imports new/changed assets (glb from the CAD pipeline).
    & dotnet build (Join-Path $GodotProject 'FtcSim.csproj') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
    $code = Invoke-Godot @('--headless', '--path', $GodotProject, '--import') 600
    if ($code -ne 0) { throw "Godot import failed (exit $code)" }
}
