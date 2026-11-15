# Applies EF Core migrations (including the dashboard T-SQL views) and exits.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'load-env.ps1')
dotnet run --project (Join-Path $root 'api/SiteRoof.Api') --launch-profile http -- --migrate-only
