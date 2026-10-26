# Builds, imports and starts the simulator window.
# Usage: ./scripts/godot_run.ps1 [-Robot gobilda_starter|robodojo_23205] [-Project workspace\RD4] [-Layout field|synthetic] [-Quality medium|low] [-NoBuild]
# -Project opens that code project (e.g. one cloned in the git panel, F6) in the editor (F4) instead of the robot's own copy.
param([string]$Robot = 'gobilda_starter', [string]$Project = '', [string]$Layout = 'field', [ValidateSet('low', 'medium')][string]$Quality = 'medium', [switch]$NoBuild)
. "$PSScriptRoot\godot_lib.ps1"

if (-not $NoBuild) { Build-GodotProject }
$gameArgs = @('--path', $GodotProject, '--', '--robot', $Robot, '--layout', $Layout, '--quality', $Quality)
if ($Project) { $gameArgs += @('--project', (Resolve-Path $Project).Path) }
exit (Invoke-Godot $gameArgs 86400)
