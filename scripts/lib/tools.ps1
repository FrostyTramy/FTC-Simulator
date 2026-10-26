# Locates the project's toolchain. Dot-source from other scripts: . "$PSScriptRoot\lib\tools.ps1"
# Each Find-* returns a path or $null. Environment variables override auto-detection.
# Windows is the target platform; the Linux branches let the same scripts run in CI containers and dev boxes.

$RepoRoot = (Resolve-Path ([IO.Path]::Combine($PSScriptRoot, '..', '..'))).Path
# $IsWindows does not exist in Windows PowerShell 5.1 (Desktop edition), which only runs on Windows.
$OnWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows

function Find-Godot {
    # GODOT_BIN, else a .NET ("mono") build in the repo root, D:\Tools\Godot or the per-user/opt install folder.
    if ($env:GODOT_BIN -and (Test-Path $env:GODOT_BIN)) { return $env:GODOT_BIN }
    if ($OnWindows) {
        $dirs = @($RepoRoot, 'D:\Tools\Godot', "$env:LOCALAPPDATA\Programs\Godot")
        $pattern = 'Godot_v4*mono*_console.exe'
    } else {
        $dirs = @($RepoRoot, '/opt/godot', (Join-Path $HOME '.local/share/godot'))
        $pattern = 'Godot_v4*mono*linux*.x86_64'
    }
    foreach ($dir in $dirs) {
        if (-not (Test-Path $dir)) { continue }
        $exe = Get-ChildItem -Path $dir -Filter $pattern -Recurse -Depth 2 -ErrorAction SilentlyContinue |
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
    $roots = if ($OnWindows) { @('C:\Program Files\Java', 'C:\Program Files\Eclipse Adoptium', 'C:\Program Files\Microsoft') }
             else { @('/usr/lib/jvm', '/opt/java') }
    foreach ($root in $roots) {
        if (Test-Path $root) {
            $candidates += Get-ChildItem -Path $root -Directory | Where-Object { $_.Name -match '17' } | ForEach-Object { $_.FullName }
        }
    }
    $javac = if ($OnWindows) { 'javac.exe' } else { 'javac' }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path ([IO.Path]::Combine($c, 'bin', $javac)))) {
            $v = Get-JdkVersion $c
            if ($v -and $v.StartsWith('17')) { return $c }
        }
    }
    return $null
}

function Find-Gradle {
    # The verified local distribution from setup_tools.ps1 (no download), else the wrapper.
    $bin = if ($OnWindows) { 'gradle.bat' } else { 'gradle' }
    $local = [IO.Path]::Combine($RepoRoot, 'generated', 'tools', 'gradle-8.14.3', 'bin', $bin)
    if (Test-Path $local) { return $local }
    $wrapper = if ($OnWindows) { 'gradlew.bat' } else { 'gradlew' }
    return [IO.Path]::Combine($RepoRoot, 'java', $wrapper)
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
    # The project venv if it exists, else the system Python.
    $venv = if ($OnWindows) { [IO.Path]::Combine($RepoRoot, '.venv', 'Scripts', 'python.exe') } else { [IO.Path]::Combine($RepoRoot, '.venv', 'bin', 'python') }
    if (Test-Path $venv) { return $venv }
    foreach ($name in @('python', 'python3')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    return $null
}

function Get-VenvSitePackages {
    # site-packages of the project venv (Lib\site-packages on Windows, lib/pythonX.Y/site-packages elsewhere).
    if ($OnWindows) { return [IO.Path]::Combine($RepoRoot, '.venv', 'Lib', 'site-packages') }
    $lib = Get-ChildItem -Path ([IO.Path]::Combine($RepoRoot, '.venv', 'lib')) -Directory -Filter 'python3*' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($lib) { return Join-Path $lib.FullName 'site-packages' }
    return $null
}
