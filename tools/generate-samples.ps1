$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ff = Join-Path $root "third_party\ffmpeg\ffmpeg.exe"
$out = Join-Path $root "testdata\samples"
if (-not (Test-Path $ff)) { throw "missing ffmpeg.exe" }
New-Item -ItemType Directory -Force -Path $out | Out-Null
$made = New-Object System.Collections.Generic.List[string]
$missed = New-Object System.Collections.Generic.List[string]

function Invoke-Sample([string]$name, [string[]]$ffmpegArgs) {
    $dest = Join-Path $out $name
    $all = @("-y", "-nostdin", "-hide_banner", "-loglevel", "error") + $ffmpegArgs + @($dest)
    & $ff @all
    if ($LASTEXITCODE -eq 0 -and (Test-Path $dest) -and (Get-Item $dest).Length -gt 32) {
        $script:made.Add($name)
        Write-Host "ok  $name"
    } else {
        $script:missed.Add($name)
        Write-Host "miss $name"
        if (Test-Path $dest) { Remove-Item $dest -Force }
    }
}

$src = "testsrc2=size=640x360:rate=25:duration=6"
$tone = "sine=frequency=440:sample_rate=48000:duration=6"
Invoke-Sample "h264.mp4" @("-f","lavfi","-i",$src,"-f","lavfi","-i",$tone,"-shortest","-c:v","libx264","-preset","ultrafast","-pix_fmt","yuv420p","-c:a","aac")
Invoke-Sample "h264-1080p.mp4" @("-f","lavfi","-i","testsrc2=size=1920x1080:rate=30:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libx264","-preset","ultrafast","-pix_fmt","yuv420p","-c:a","aac")
Invoke-Sample "h264-4k.mp4" @("-f","lavfi","-i","testsrc2=size=3840x2160:rate=24:duration=1","-f","lavfi","-i","sine=frequency=440:duration=1","-shortest","-c:v","libx264","-preset","ultrafast","-pix_fmt","yuv420p","-c:a","aac")
Invoke-Sample "h264-60fps.mp4" @("-f","lavfi","-i","testsrc2=size=1280x720:rate=60:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libx264","-preset","ultrafast","-pix_fmt","yuv420p","-c:a","aac")
Invoke-Sample "hevc.mp4" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libx265","-preset","ultrafast","-pix_fmt","yuv420p","-c:a","aac","-tag:v","hvc1")
Invoke-Sample "hevc-10bit.mp4" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-c:v","libx265","-preset","ultrafast","-pix_fmt","yuv420p10le","-x265-params","log-level=error","-an")
Invoke-Sample "hevc-hdr.mp4" @("-f","lavfi","-i","testsrc2=size=640x360:rate=24:duration=2","-c:v","libx265","-preset","ultrafast","-pix_fmt","yuv420p10le","-color_primaries","bt2020","-color_trc","smpte2084","-colorspace","bt2020nc","-x265-params","log-level=error","-an")
Invoke-Sample "av1.mp4" @("-f","lavfi","-i","testsrc2=size=640x360:rate=24:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libsvtav1","-preset","12","-crf","40","-c:a","aac")
Invoke-Sample "vp9.webm" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libvpx-vp9","-deadline","realtime","-cpu-used","8","-b:v","400k","-c:a","libopus")
Invoke-Sample "vp8.webm" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libvpx","-deadline","realtime","-cpu-used","8","-b:v","400k","-c:a","libvorbis")
Invoke-Sample "mpeg2.mpg" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","mpeg2video","-b:v","2M","-c:a","mp2")
Invoke-Sample "mpeg1.mpg" @("-f","lavfi","-i","testsrc2=size=320x240:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","mpeg1video","-b:v","1M","-c:a","mp2")
Invoke-Sample "mpeg4.avi" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","mpeg4","-q:v","5","-c:a","libmp3lame")
Invoke-Sample "msmpeg4.avi" @("-f","lavfi","-i","testsrc2=size=320x240:rate=15:duration=2","-c:v","msmpeg4","-q:v","5","-an")
Invoke-Sample "wmv.wmv" @("-f","lavfi","-i","testsrc2=size=320x240:rate=15:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","wmv2","-b:v","500k","-c:a","wmav2")
Invoke-Sample "prores.mov" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=1","-c:v","prores_ks","-profile:v","0","-an")
Invoke-Sample "flv.flv" @("-f","lavfi","-i","testsrc2=size=320x240:rate=15:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","flv","-b:v","400k","-c:a","aac")
Invoke-Sample "h264.mov" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libx264","-preset","ultrafast","-c:a","aac")
Invoke-Sample "h264.m4v" @("-f","lavfi","-i","testsrc2=size=320x240:rate=25:duration=2","-c:v","libx264","-preset","ultrafast","-an")
Invoke-Sample "h264.ts" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","libx264","-preset","ultrafast","-c:a","aac","-f","mpegts")
Invoke-Sample "h264.m2ts" @("-f","lavfi","-i","testsrc2=size=640x360:rate=25:duration=2","-c:v","libx264","-preset","ultrafast","-an","-f","mpegts")
if (Test-Path (Join-Path $out "h264.m2ts")) {
    Copy-Item (Join-Path $out "h264.m2ts") (Join-Path $out "h264.mts") -Force
    $made.Add("h264.mts")
}
Invoke-Sample "mpeg2.vob" @("-f","lavfi","-i","testsrc2=size=720x480:rate=25:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","mpeg2video","-b:v","2M","-c:a","ac3","-f","vob")
Invoke-Sample "h263.3gp" @("-f","lavfi","-i","testsrc2=size=176x144:rate=15:duration=2","-f","lavfi","-i","sine=frequency=440:duration=2","-shortest","-c:v","h263","-c:a","aac")
Invoke-Sample "mpeg2.mxf" @("-f","lavfi","-i","testsrc2=size=720x480:rate=25:duration=2","-c:v","mpeg2video","-b:v","2M","-an","-f","mxf")
Invoke-Sample "rv20.rm" @("-f","lavfi","-i","testsrc2=size=320x240:rate=15:duration=2","-c:v","rv20","-b:v","300k","-an","-f","rm")
Invoke-Sample "rv20.rmvb" @("-f","lavfi","-i","testsrc2=size=320x240:rate=15:duration=2","-c:v","rv20","-b:v","300k","-an","-f","rm")
Invoke-Sample "h264.mkv" @("-i",(Join-Path $out "h264.mp4"),"-c","copy")

Invoke-Sample "tone.mp3" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","libmp3lame","-q:a","5")
Invoke-Sample "tone.m4a" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","aac")
Invoke-Sample "tone.flac" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","flac")
Invoke-Sample "tone.wav" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","pcm_s16le")
Invoke-Sample "tone.ogg" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","libvorbis")
Invoke-Sample "tone.opus" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","libopus")
Invoke-Sample "tone.ac3" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","ac3")
Invoke-Sample "tone.eac3" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","eac3")
Invoke-Sample "tone.wma" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","wmav2")
Invoke-Sample "tone.mka" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","alac")
Invoke-Sample "tone-alac.m4a" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","alac")
Invoke-Sample "tone.dts" @("-f","lavfi","-i","sine=frequency=440:duration=2","-c:a","dca","-strict","-2")

Invoke-Sample "dual-audio.mkv" @(
    "-f","lavfi","-i","testsrc2=size=320x240:rate=25:duration=3",
    "-f","lavfi","-i","sine=frequency=440:duration=3",
    "-f","lavfi","-i","sine=frequency=880:duration=3",
    "-map","0:v","-map","1:a","-map","2:a",
    "-c:v","libx264","-preset","ultrafast","-c:a","aac",
    "-metadata:s:a:0","language=eng","-metadata:s:a:1","language=chi"
)

function U([int[]]$codes) { -join ($codes | ForEach-Object { [char]$_ }) }
$utf8 = New-Object System.Text.UTF8Encoding $false
$line = U @(0x4E2D,0x6587,0x6D4B,0x8BD5,0x5B57,0x5E55)
$assLine = (U @(0x4E2D,0x6587)) + " ASS " + (U @(0x5B57,0x5E55))
$vttLine = (U @(0x4E2D,0x6587)) + " WebVTT"
$gbLine = U @(0x7B80,0x4F53,0x4E2D,0x6587,0x7F16,0x7801)
$srt = "1`r`n00:00:00,200 --> 00:00:05,000`r`n$line`r`n"
[System.IO.File]::WriteAllText((Join-Path $out "h264.srt"), $srt, $utf8)
[System.IO.File]::WriteAllText((Join-Path $out "external.vtt"), "WEBVTT`r`n`r`n00:00.200 --> 00:05.000`r`n$vttLine`r`n", $utf8)
$ass = @"
[Script Info]
ScriptType: v4.00+

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Microsoft YaHei,28,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,20,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.20,0:00:05.00,Default,,0,0,0,,$assLine
"@
[System.IO.File]::WriteAllText((Join-Path $out "styled.ass"), $ass, $utf8)
$gb = [System.Text.Encoding]::GetEncoding(54936)
$gbBody = "1`r`n00:00:00,200 --> 00:00:05,000`r`n$gbLine`r`n"
[System.IO.File]::WriteAllBytes((Join-Path $out "gb18030.srt"), $gb.GetBytes($gbBody))
if (Test-Path (Join-Path $out "h264.mp4")) {
    Copy-Item (Join-Path $out "h264.mp4") (Join-Path $out "gb18030.mp4") -Force
}

Invoke-Sample "embedded-srt.mkv" @("-i",(Join-Path $out "h264.mp4"),"-i",(Join-Path $out "h264.srt"),"-map","0","-map","1","-c","copy","-c:s","srt","-metadata:s:s:0","language=chi")
Invoke-Sample "embedded-ass.mkv" @("-i",(Join-Path $out "h264.mp4"),"-i",(Join-Path $out "styled.ass"),"-map","0","-map","1","-c","copy","-c:s","ass","-metadata:s:s:0","language=chi")

$dirName = U @(0x4E2D,0x6587,0x76EE,0x5F55)
$fileName = (U @(0x5F71,0x7247)) + " " + (U @(0x6D4B,0x8BD5)) + ".mp4"
$dir = Join-Path $out $dirName
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Copy-Item (Join-Path $out "h264.mp4") (Join-Path $dir $fileName) -Force
$made.Add("unicode-path")

$bytes = New-Object byte[] 4096
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[System.IO.File]::WriteAllBytes((Join-Path $out "broken.mp4"), $bytes)
$made.Add("broken.mp4")

$report = [ordered]@{ made = $made; missed = $missed }
$report | ConvertTo-Json | Set-Content (Join-Path $out "manifest.json") -Encoding UTF8
Write-Host ("made " + $made.Count + " missed " + $missed.Count)
