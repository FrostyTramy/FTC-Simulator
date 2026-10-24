# Checks the development toolchain and prints one line per tool. Exit code 1 if anything required is missing.
# Usage: ./scripts/check_tools.ps1      Install steps for anything missing: docs/SETUP.md
. "$PSScriptRoot\lib\tools.ps1"

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$tool, [bool]$ok, [string]$detail, [bool]$required = $true) {
    $results.Add([pscustomobject]@{ Tool = $tool; Status = $(if ($ok) { 'OK' } elseif ($required) { 'MISSING' } else { 'optional' }); Detail = $detail; Required = $required })
}

# Git
$git = Get-Command git -ErrorAction SilentlyContinue
Add-Result 'Git' ([bool]$git) $(if ($git) { (& git --version) } else { 'not on PATH' })

# Godot 4 .NET
$godot = Find-Godot
if ($godot) {
    $lines = Invoke-WithTimeout $godot @('--headless', '--version') 20
    if ($null -eq $lines) {
        Add-Result 'Godot 4 .NET' $false "found but did not answer --version within 20 s ($godot)"
    } else {
        $v = $lines | Select-Object -Last 1
        Add-Result 'Godot 4 .NET' ($v -match '^4\.\d+.*mono') "$v ($godot)"
    }
} else {
    Add-Result 'Godot 4 .NET' $false 'no Godot_v4*mono*_console.exe found; set GODOT_BIN'
}

# .NET SDK (8 or newer builds Godot's net8.0 projects)
$sdks = @()
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $sdks = @(& dotnet --list-sdks) }
$okSdk = @($sdks | Where-Object { [int](($_ -split '\.')[0]) -ge 8 }).Count -gt 0
Add-Result '.NET SDK >= 8' $okSdk $(if ($sdks.Count) { ($sdks | ForEach-Object { ($_ -split ' ')[0] }) -join ', ' } else { 'no SDK (runtimes only?)' })

# JDK 17
$jdk = Find-Jdk17
Add-Result 'JDK 17' ([bool]$jdk) $(if ($jdk) { "$(Get-JdkVersion $jdk) ($jdk)" } else { 'no JDK 17 found; set JAVA17_HOME' })

# Python 3 + project packages
$py = Find-Python
if ($py) {
    $pyVersion = (& $py -c 'import sys; print(sys.version.split()[0])')
    Add-Result 'Python 3' ($pyVersion -match '^3\.(1[1-9])') "$pyVersion ($py)"
    $req = Join-Path $RepoRoot 'tools\cad_pipeline\requirements.txt'
    $missing = (& $py "$PSScriptRoot\lib\check_requirements.py" $req)
    $isVenv = $py -like "$RepoRoot*"
    $detail = if ($missing) { $missing } else { 'all pinned packages present' }
    if (-not $isVenv) { $detail = "no .venv yet; $detail" }
    Add-Result 'Python packages' ($isVenv -and -not $missing) $detail
} else {
    Add-Result 'Python 3' $false 'python not found'
}

# Node (only for the Godot MCP server used by the AI assistant)
$node = Get-Command node -ErrorAction SilentlyContinue
Add-Result 'Node 18+ (Godot MCP)' ([bool]$node) $(if ($node) { (& node --version) } else { 'not on PATH' }) $false

$results | Format-Table Tool, Status, Detail -AutoSize -Wrap | Out-String -Width 200 | Write-Host
$failed = @($results | Where-Object { $_.Required -and $_.Status -ne 'OK' })
if ($failed.Count) { Write-Host "$($failed.Count) required tool(s) missing - see docs/SETUP.md"; exit 1 }
Write-Host 'All required tools OK'
exit 0
