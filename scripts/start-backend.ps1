$ErrorActionPreference = "Stop"
$backend = Join-Path (Split-Path -Parent $PSScriptRoot) "backend"

Push-Location $backend
try {
    go run .
}
finally {
    Pop-Location
}
