param(
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$web = Join-Path (Split-Path -Parent $PSScriptRoot) "web"

Push-Location $web
try {
    if (-not $SkipInstall -and -not (Test-Path -LiteralPath "node_modules")) {
        npm install
    }
    npm run dev
}
finally {
    Pop-Location
}
