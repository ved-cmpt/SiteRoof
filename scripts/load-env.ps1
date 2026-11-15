# Loads KEY=VALUE pairs from the repository .env file into the current PowerShell session.
# Usage: . ./scripts/load-env.ps1
$envFile = Join-Path (Split-Path $PSScriptRoot -Parent) '.env'
if (-not (Test-Path $envFile)) {
    Write-Warning "No .env file found. Copy .env.example to .env first."
    return
}
foreach ($line in Get-Content $envFile) {
    if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
    $idx = $line.IndexOf('=')
    $name = $line.Substring(0, $idx).Trim()
    $value = $line.Substring($idx + 1).Trim().Trim('"')
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
}
