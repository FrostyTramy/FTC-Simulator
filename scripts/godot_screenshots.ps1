# Renders the 4 preset views (audience, red, blue, top-down) to PNG and measures fps / draw calls
# per view with vsync off. Needs a desktop session (rendering does not run headless).
# Usage: ./scripts/godot_screenshots.ps1 [-Layout field] [-Quality medium|low] [-OutDir reports/screenshots/...]
param([string]$Layout = 'field', [ValidateSet('low', 'medium')][string]$Quality = 'medium', [string]$OutDir = '', [double]$Seconds = 3, [switch]$NoBuild)
. "$PSScriptRoot\godot_lib.ps1"

if (-not $OutDir) { $OutDir = Join-Path $RepoRoot "reports\screenshots\${Layout}_$Quality" }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (-not $NoBuild) { Build-GodotProject }
$code = Invoke-Godot @('--path', $GodotProject, '--', '--layout', $Layout, '--quality', $Quality, '--seconds', "$Seconds", '--screenshot', $OutDir) 600
Write-Host "screenshots: $OutDir (exit $code)"
exit $code
