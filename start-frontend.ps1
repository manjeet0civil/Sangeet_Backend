# =====================================================================
#  Sangeet — start ONLY the frontend (React / Vite).
#  Port comes from MusicWebsiteFrontEnd\.env (VITE_PORT), the API it calls
#  comes from VITE_API_URL in the same file.
#
#  Usage:
#    ./start-frontend.ps1            # dev server (hot reload)     -> VITE_PORT
#    ./start-frontend.ps1 -Prod      # build + serve dist/          -> VITE_PREVIEW_PORT
# =====================================================================
param([switch]$Prod)

$ErrorActionPreference = "Stop"
$web = Join-Path $PSScriptRoot "MusicWebsiteFrontEnd"

Push-Location $web
try {
    if (-not (Test-Path node_modules)) {
        Write-Host "Installing dependencies..." -ForegroundColor Cyan
        npm install
    }

    if ($Prod) {
        Write-Host "Building production bundle (uses .env.production)..." -ForegroundColor Cyan
        npm run build
        Write-Host "Serving the build - see the URL printed below." -ForegroundColor Green
        npm run preview
    } else {
        Write-Host "Starting the dev server (uses .env)..." -ForegroundColor Green
        npm run dev
    }
} finally { Pop-Location }
