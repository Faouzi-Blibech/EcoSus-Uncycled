# Puts the game online from this laptop. The game server (Docker, port 5000) and the photo service
# (port 8000) run here; two Cloudflare quick tunnels give them https addresses; the web build on
# Cloudflare Pages gets a config.json with those addresses. The web, Android and Windows builds read
# that file at startup, so they always find the server even though quick-tunnel addresses change.
#
#   .\deploy\online.ps1            build the web page, start or reuse everything, publish
#   .\deploy\online.ps1 -NoBuild   publish the existing build/web with the current addresses
#   .\deploy\online.ps1 -Stop      stop the tunnels (the game goes offline)
#
# Needs once: Docker Desktop running, Python with requirements.txt, `npx wrangler login`.
# Run it again after a reboot or if a tunnel dies: healthy tunnels are reused, dead ones replaced.
# The laptop must stay on, awake and online while people play.

#   .\deploy\online.ps1 -NoBuild -GameServer wss://uncycled-server.onrender.com/ws
#                                  the game server runs in the cloud: no local server or game tunnel,
#                                  only the photo service stays on this laptop

param(
    [switch]$NoBuild,
    [switch]$Stop,
    [string]$Project = "uncycled",
    [string]$Site = "",
    [string]$GameServer = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$run = Join-Path $root "build\online"
New-Item -ItemType Directory -Force $run | Out-Null
if (-not $Site) { $Site = "https://$Project.pages.dev" }
$wrangler = "wrangler@4"

function Find-Cloudflared {
    $candidates = @($env:CLOUDFLARED, (Join-Path $HOME "Tools\cloudflared\cloudflared.exe"))
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    $cmd = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "cloudflared not found. Put cloudflared.exe in $HOME\Tools\cloudflared or set `$env:CLOUDFLARED."
}

function Test-Url([string]$url) {
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        return $r.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Wait-Url([string]$url, [int]$seconds, [string]$what) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        if (Test-Url $url) {
            Write-Host "  $what is up" -ForegroundColor Green
            return
        }
        Start-Sleep -Seconds 2
    }
    throw "$what did not answer at $url within $seconds s."
}

function Stop-Tunnel([string]$name) {
    $pidFile = Join-Path $run "$name.pid"
    if (Test-Path $pidFile) {
        $id = [int](Get-Content $pidFile)
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        Remove-Item $pidFile, (Join-Path $run "$name.url") -ErrorAction SilentlyContinue
    }
}

# Returns the tunnel's https address. Reuses a running tunnel whose address still answers.
function Start-Tunnel([string]$name, [int]$port, [string]$healthPath) {
    $pidFile = Join-Path $run "$name.pid"
    $urlFile = Join-Path $run "$name.url"
    if ((Test-Path $pidFile) -and (Test-Path $urlFile)) {
        $alive = Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue
        $url = (Get-Content $urlFile).Trim()
        if ($alive -and (Test-Url "$url$healthPath")) {
            Write-Host "  $name tunnel reused: $url" -ForegroundColor Green
            return $url
        }
    }
    Stop-Tunnel $name
    $log = Join-Path $run "$name.log"
    Remove-Item $log -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $script:cloudflared -WindowStyle Hidden -PassThru `
        -ArgumentList @("tunnel", "--no-autoupdate", "--url", "http://127.0.0.1:$port") `
        -RedirectStandardError $log -RedirectStandardOutput (Join-Path $run "$name.out.log")
    Set-Content -Path $pidFile -Value $p.Id
    $end = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $end) {
        Start-Sleep -Seconds 1
        if (Test-Path $log) {
            $m = Select-String -Path $log -Pattern "https://[a-z0-9-]+\.trycloudflare\.com" | Select-Object -First 1
            if ($m) {
                $url = $m.Matches[0].Value
                Set-Content -Path $urlFile -Value $url
                Write-Host "  $name tunnel: $url" -ForegroundColor Green
                return $url
            }
        }
        if ($p.HasExited) { break }
    }
    throw "The $name tunnel did not start. See $log"
}

if ($Stop) {
    Stop-Tunnel "game"
    Stop-Tunnel "scan"
    Write-Host "Tunnels stopped: the game is offline." -ForegroundColor Yellow
    exit 0
}

$script:cloudflared = Find-Cloudflared

Write-Host "1/5 Game server" -ForegroundColor Cyan
if ($GameServer) {
    if ($GameServer -notmatch "^wss?://[^/\s]+/ws$") { throw "-GameServer must look like wss://host/ws" }
    $cloudHealth = ($GameServer -replace "^wss://", "https://" -replace "^ws://", "http://") -replace "/ws$", "/health"
    # A free cloud instance may be asleep: the first request wakes it (about a minute).
    Wait-Url $cloudHealth 180 "Cloud game server"
} else {
    if (-not (Test-Url "http://127.0.0.1:5000/health")) {
        docker compose up -d --build server
        if ($LASTEXITCODE -ne 0) { throw "Docker could not start the game server. Is Docker Desktop running?" }
    }
    Wait-Url "http://127.0.0.1:5000/health" 120 "Game server"
}

Write-Host "2/5 Photo service (port 8000) and its tunnel" -ForegroundColor Cyan
$scan = Start-Tunnel "scan" 8000 "/health"
if (-not (Test-Url "http://127.0.0.1:8000/health")) {
    $python = (Get-Command python -ErrorAction Stop).Source
    $env:ECO_PUBLIC_URL = $scan
    Start-Process -FilePath $python -WorkingDirectory $root -WindowStyle Hidden `
        -ArgumentList @("-m", "uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000") `
        -RedirectStandardOutput (Join-Path $run "photo.out.log") -RedirectStandardError (Join-Path $run "photo.log") | Out-Null
}
Wait-Url "http://127.0.0.1:8000/health" 180 "Photo service (models load in ~30 s)"

Write-Host "3/5 Game server tunnel" -ForegroundColor Cyan
if ($GameServer) {
    Stop-Tunnel "game"
    Write-Host "  not needed: the game server is $GameServer" -ForegroundColor Green
} else {
    $game = Start-Tunnel "game" 5000 "/health"
    Wait-Url "$game/health" 90 "Game server through the tunnel"
}
Wait-Url "$scan/health" 90 "Photo service through the tunnel"

Write-Host "4/5 Web build" -ForegroundColor Cyan
if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot "build-web.ps1")
}
$web = Join-Path $root "build\web"
if (-not (Test-Path (Join-Path $web "index.html"))) { throw "No build/web. Run without -NoBuild." }
$server = if ($GameServer) { $GameServer } else { "wss://" + ($game -replace "^https://", "") + "/ws" }
$config = [ordered]@{ server = $server; scan = $scan; updated = (Get-Date).ToUniversalTime().ToString("s") + "Z" }
($config | ConvertTo-Json) | Set-Content -Encoding ascii (Join-Path $web "config.json")

Write-Host "5/5 Publish to Cloudflare Pages ($Project)" -ForegroundColor Cyan
npx --yes $wrangler pages deploy $web --project-name $Project --branch main --commit-dirty=true
if ($LASTEXITCODE -ne 0) { throw "Pages upload failed. Run 'npx wrangler login' once, then try again." }

$end = (Get-Date).AddSeconds(90)
$live = $false
while ((Get-Date) -lt $end -and -not $live) {
    try {
        $c = Invoke-WebRequest -Uri "$Site/config.json?t=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())" -UseBasicParsing -TimeoutSec 5
        $live = $c.Content -match [regex]::Escape($server)
    } catch { }
    if (-not $live) { Start-Sleep -Seconds 3 }
}
if (-not $live) { throw "$Site/config.json does not show the new server address yet." }

Write-Host ""
Write-Host "ONLINE" -ForegroundColor Green
Write-Host "  Play (web)     : $Site"
Write-Host "  Game server    : $server"
Write-Host "  Photo service  : $scan"
Write-Host "  Keep this laptop on, awake and online. Stop: .\deploy\online.ps1 -Stop"
