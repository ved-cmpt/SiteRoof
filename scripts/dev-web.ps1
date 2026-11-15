# Runs the Angular dev server on http://localhost:4200, proxying /api to http://localhost:5080.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location (Join-Path $root 'web')
try { npm start } finally { Pop-Location }
