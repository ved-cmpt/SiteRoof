# Starts SQL Server (docker compose) and runs the API on http://localhost:5080 in Development.
# The API applies migrations on startup. Usage: ./scripts/dev-api.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'load-env.ps1')
docker compose -f (Join-Path $root 'docker-compose.yml') up -d db
dotnet run --project (Join-Path $root 'api/SiteRoof.Api') --launch-profile http
