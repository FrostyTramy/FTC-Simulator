# Locates the project's toolchain. Dot-source from other scripts: . "$PSScriptRoot\lib\tools.ps1"
# Each Find-* returns a path or $null. Environment variables override auto-detection.

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path

function Find-Godot {
    # GODOT_BIN, else a .NET ("mono") console build in the repo root or D:\Tools\Godot.
    if ($env:GODOT_BIN -and (Test-Path $env:GODOT_BIN)) { return $env:GODOT_BIN }
    foreach ($dir in @($RepoRoot, 'D:\Tools\Godot', "$env:LOCALAPPDATA\Programs\Godot")) {
        if (-not (Test-Path $dir)) { continue }
        $exe = Get-ChildItem -Path $dir -Filter 'Godot_v4*mono*_console.exe' -Recurse -Depth 2 -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($exe) { return $exe.FullName }
    }
    return $null
}

function Get-JdkVersion([string]$jdkHome) {
    # Reads JAVA_VERSION from the JDK's release file (no process start; java.exe can hang under redirection).
    $release = Join-Path $jdkHome 'release'
    if (-not (Test-Path $release)) { return $null }
    $line = Select-String -Path $release -Pattern '^JAVA_VERSION="([^"]+)"' | Select-Object -First 1
    if ($line) { return $line.Matches[0].Groups[1].Value }
    return $null
}

function Find-Jdk17 {
    # JAVA17_HOME, else JAVA_HOME if it is 17, else standard install folders. PATH java is ignored on purpose.
    $candidates = @($env:JAVA17_HOME, $env:JAVA_HOME)
    foreach ($root in @('C:\Program Files\Java', 'C:\Program Files\Eclipse Adoptium', 'C:\Program Files\Microsoft')) {
        if (Test-Path $root) {
            $candidates += Get-ChildItem -Path $root -Directory -Filter 'jdk-17*' | ForEach-Object { $_.FullName }
        }
    }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path "$c\bin\javac.exe")) {
            $v = Get-JdkVersion $c
            if ($v -and $v.StartsWith('17')) { return $c }
        }
    }
    return $null
}

function Invoke-WithTimeout([string]$exe, [string[]]$arguments, [int]$seconds = 20) {
    # Runs a native program and returns its stdout lines, or $null if it does not exit in time (then kills it).
    $out = [IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath $exe -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput $out
        if (-not $p.WaitForExit($seconds * 1000)) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            return $null
        }
        return @(Get-Content $out)
    } finally {
        Remove-Item $out -ErrorAction SilentlyContinue
    }
}

function Find-Python {
    # The project venv if it exists, else the system Python launcher target.
    $venv = Join-Path $RepoRoot '.venv\Scripts\python.exe'
    if (Test-Path $venv) { return $venv }
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}
