# Compiles the Godot C# project and imports new or changed assets (run after tools/cad_pipeline/run.py).
. "$PSScriptRoot\godot_lib.ps1"
Build-GodotProject
Write-Host 'Godot project built and assets imported'
