$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dl = Join-Path $root "third_party\downloads"
$mpvOut = Join-Path $root "third_party\mpv\win-x64"
$ffOut = Join-Path $root "third_party\ffmpeg"
New-Item -ItemType Directory -Force -Path $dl, $mpvOut, $ffOut | Out-Null

function Find-SevenZip {
    $cmd = Get-Command 7z -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $known = "C:\Program Files\7-Zip\7z.exe"
    if (Test-Path $known) { return $known }
    $zr = Join-Path $dl "7zr.exe"
    if (-not (Test-Path $zr)) {
        Invoke-WebRequest -Uri "https://www.7-zip.org/a/7zr.exe" -OutFile $zr
    }
    return $zr
}

function Fetch([string]$url, [string]$name) {
    $dest = Join-Path $dl $name
    if (-not (Test-Path $dest) -or (Get-Item $dest).Length -lt 1000000) {
        Write-Host "download $name"
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try { Invoke-WebRequest -Uri $url -OutFile $dest; break }
            catch { if ($attempt -eq 3) { throw }; Start-Sleep -Seconds 3 }
        }
    }
    return $dest
}

$seven = Find-SevenZip
$tag = "20261021"
$base = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$tag"
$mpv = Fetch "$base/mpv-dev-x86_64-$tag-git-e471496c56.7z" "mpv-dev-x86_64.7z"
$ff = Fetch "$base/ffmpeg-x86_64-git-9a861e968.7z" "ffmpeg-x86_64.7z"
# SHA-256 of the exact source archives used for the verified build.
$expectedMpv = "EFC719EFF3DC5954B4A5EFC800632808BCEC01CC9034940DD14E5EB7FE9EA633"
$expectedFfmpeg = "FC9C135B799B410FFC78517F428904C299BC64CD8144F21F63F84FCD8C35EA19"
if ((Get-FileHash -LiteralPath $mpv -Algorithm SHA256).Hash -ne $expectedMpv) { throw "libmpv archive SHA-256 mismatch" }
if ((Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ne $expectedFfmpeg) { throw "FFmpeg archive SHA-256 mismatch" }
& $seven x $mpv "-o$mpvOut" -y "libmpv-2.dll" "include" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "mpv extract failed" }
& $seven e $ff "-o$ffOut" -y "ffmpeg.exe" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ffmpeg extract failed" }

$vulkan = Join-Path $mpvOut "vulkan-1.dll"
if (-not (Test-Path $vulkan)) {
    $sys = "C:\Windows\System32\vulkan-1.dll"
    if (Test-Path $sys) {
        Copy-Item $sys $vulkan
        Write-Host "copied vulkan-1.dll from System32"
    } else {
        throw "vulkan-1.dll is missing. Install the Vulkan Runtime or copy the x64 loader to $vulkan"
    }
}
Write-Host "ready"
