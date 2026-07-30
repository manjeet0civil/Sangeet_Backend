# =====================================================================
#  Sangeet — start ONLY the backend API.
#  Host/port come from MusicWebsite\MusicWebsite\.env (BACKEND_HOST / BACKEND_PORT).
#  Usage:  ./start-backend.ps1
# =====================================================================
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$api  = Join-Path $root "MusicWebsite\MusicWebsite"

# Read the port out of .env just so we can print the right URL.
$envFile = Join-Path $api ".env"
$port = "5000"
if (Test-Path $envFile) {
    $match = Select-String -Path $envFile -Pattern '^\s*BACKEND_PORT\s*=\s*(\d+)' | Select-Object -First 1
    if ($match) { $port = $match.Matches[0].Groups[1].Value }
} else {
    Write-Host "No .env found at $envFile - using defaults (port 5000)." -ForegroundColor Yellow
}

Write-Host "Starting API on http://localhost:$port ..." -ForegroundColor Green
Push-Location $api
try { dotnet run --project MusicWebsite.csproj } finally { Pop-Location }
