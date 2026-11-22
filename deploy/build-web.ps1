# Web build for Cloudflare Pages: exports the "Web" preset to build/web, then gzips the two files
# Pages cannot take (25 MB limit per file): index.wasm and index.pck become index.wasm.gz and
# index.pck.gz. The page unzips them in the browser (script in the preset's html/head_include).
#
#   .\deploy\build-web.ps1
#
# deploy/online.ps1 adds config.json (the live server addresses) and uploads the folder.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Find-Godot {
    $candidates = @($env:GODOT, (Join-Path $HOME "Tools\godot\godot.exe"), (Join-Path $HOME "tools\godot\Godot_v4.7.2-stable_win64.exe"))
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    throw "Godot not found. Set `$env:GODOT to the Godot 4.7.2 executable."
}

$godot = Find-Godot
$out = Join-Path $root "build\web"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "Exporting the web build with $godot ..." -ForegroundColor Cyan
& $godot --headless --path client --export-release "Web" ../build/web/index.html 2>&1 | Out-Null
foreach ($f in @("index.html", "index.js", "index.wasm", "index.pck")) {
    if (-not (Test-Path (Join-Path $out $f))) { throw "Export failed: build/web/$f is missing." }
}

# gzip the big files (Pages: 25 MB per file).
Add-Type -AssemblyName System.IO.Compression
foreach ($f in @("index.wasm", "index.pck")) {
    $src = Join-Path $out $f
    $dst = "$src.gz"
    $in = [System.IO.File]::OpenRead($src)
    $fs = [System.IO.File]::Create($dst)
    $gz = New-Object System.IO.Compression.GZipStream($fs, [System.IO.Compression.CompressionLevel]::Optimal)
    $in.CopyTo($gz)
    $gz.Dispose(); $fs.Dispose(); $in.Dispose()
    $mb = (Get-Item $dst).Length / 1MB
    if ($mb -ge 25) { throw "$f.gz is $([math]::Round($mb, 1)) MB, over the 25 MB Pages limit." }
    Remove-Item $src
    Write-Host ("  {0}.gz  {1:N1} MB" -f $f, $mb)
}

# config.json changes on every tunnel restart: never cache it.
"/config.json`n  Cache-Control: no-store`n" | Set-Content -Encoding ascii (Join-Path $out "_headers")

Write-Host "Web build ready in build/web" -ForegroundColor Green
