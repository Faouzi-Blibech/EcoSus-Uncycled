# Pitch demo: start the game server and the photo service on this PC, with scan links (QR codes)
# that a phone on the same Wi-Fi can open.
#
#   .\start-demo.ps1          start both (Docker Desktop must be running)
#   .\start-demo.ps1 -Game    also open the game client (Godot 4.7.2)
#
# If the phone says "site can't be reached": same Wi-Fi, mobile data and VPN off. Some routers keep
# devices apart; then put this PC on the phone's hotspot and run the script again.
# Build the images once, well before the pitch: the first build downloads ~2 GB. (On Windows, if git
# rewrites requirements.txt with CRLF line endings, Docker's cache misses and downloads it all again.)

param([switch]$Game)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# This PC's address on the network that reaches the internet (the Wi-Fi or a phone's hotspot).
$net = Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq "Up" } | Select-Object -First 1
if ($null -eq $net) {
    Write-Host "No network found. Connect this PC to Wi-Fi (or your phone's hotspot) first." -ForegroundColor Red
    exit 1
}
$ip = $net.IPv4Address.IPAddress
$env:ECO_PUBLIC_URL = "http://${ip}:8000"

# A game server already answering on port 5000 (started by hand) is used as is: a second one could
# not take the port.
$services = @("server", "verifier")
try {
    Invoke-WebRequest -Uri "http://localhost:5000/health" -UseBasicParsing -TimeoutSec 3 | Out-Null
    Write-Host "A game server is already running on port 5000: using it." -ForegroundColor Yellow
    $services = @("verifier")
} catch { }

Write-Host "Starting $($services -join ' + ') on $ip ..." -ForegroundColor Cyan
docker compose up -d --build @services
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker could not start the services. Is Docker Desktop running?" -ForegroundColor Red
    exit 1
}

function Wait-Up([string]$url, [string]$name) {
    for ($i = 0; $i -lt 90; $i++) {
        try {
            Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 | Out-Null
            Write-Host "  $name is up" -ForegroundColor Green
            return $true
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    Write-Host "  $name did not answer at $url" -ForegroundColor Red
    return $false
}
$serverUp = Wait-Up "http://localhost:5000/health" "Game server"
$scanUp = Wait-Up "http://localhost:8000/health" "Photo service (models load in ~30 s)"

Write-Host ""
Write-Host "Ready for the demo:" -ForegroundColor Cyan
Write-Host "  Game on this PC      : start the client (or .\start-demo.ps1 -Game), Create a game, menu -> Scan"
Write-Host "  Scan QR codes point  : $env:ECO_PUBLIC_URL"
Write-Host "  Phone test page      : open http://${ip}:8000 on this PC -> 'Test on my phone'"
Write-Host "  Game on a phone/APK  : Settings -> server ws://${ip}:5000/ws, scan service http://${ip}:8000"

if ($Game) {
    $godot = $env:GODOT
    if (-not $godot) { $godot = Join-Path $HOME "tools\godot\Godot_v4.7.2-stable_win64.exe" }
    if (Test-Path $godot) {
        Start-Process -FilePath $godot -ArgumentList @("--path", (Join-Path $PSScriptRoot "client"))
    } else {
        Write-Host "Godot not found at $godot (set `$env:GODOT to its path)." -ForegroundColor Yellow
    }
}
if (-not ($serverUp -and $scanUp)) { exit 1 }
