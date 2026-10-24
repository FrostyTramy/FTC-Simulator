# Builds, imports and starts the simulator window.
# Usage: ./scripts/godot_run.ps1 [-Layout field|synthetic] [-Quality medium|low] [-NoBuild]
param([string]$Layout = 'field', [ValidateSet('low', 'medium')][string]$Quality = 'medium', [switch]$NoBuild)
. "$PSScriptRoot\godot_lib.ps1"

if (-not $NoBuild) { Build-GodotProject }
exit (Invoke-Godot @('--path', $GodotProject, '--', '--layout', $Layout, '--quality', $Quality) 86400)
