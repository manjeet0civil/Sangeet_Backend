# =====================================================================
#  Sangeet — start BOTH apps, each in its own window, on its own port.
#
#    Backend  (API)      -> MusicWebsite\MusicWebsite\.env   : BACKEND_PORT   (default 5000)
#    Frontend (React)    -> MusicWebsiteFrontEnd\.env        : VITE_PORT      (default 5173)
#
#  They are now independent apps: you can deploy them to two different servers.
#  The frontend finds the API through VITE_API_URL in its .env.
#
#  Usage:
#    ./serve-all.ps1            # dev mode   (frontend hot-reloads)
#    ./serve-all.ps1 -Prod      # prod mode  (frontend built, then served)
# =====================================================================
param([switch]$Prod)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "[1/2] Launching the backend in a new window..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    "-NoExit", "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $root "start-backend.ps1")
)

Write-Host "[2/2] Launching the frontend in a new window..." -ForegroundColor Cyan
$feArgs = @("-NoExit", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "start-frontend.ps1"))
if ($Prod) { $feArgs += "-Prod" }
Start-Process powershell -ArgumentList $feArgs

Write-Host ""
Write-Host "Both are starting in separate windows." -ForegroundColor Green
Write-Host "Open the frontend URL printed in its window (default http://localhost:5173)." -ForegroundColor Green
Write-Host "Close a window to stop that side; the other keeps running." -ForegroundColor DarkGray
