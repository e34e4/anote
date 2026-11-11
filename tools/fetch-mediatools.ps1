# Downloads Android ffmpeg + yt-dlp binaries into Maui Raw assets for APK shipping.
# Usage: pwsh tools/fetch-mediatools.ps1
# CI runs this before `dotnet publish` for Android.

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $Root "src/SilentNotes.Blazor/Resources/Raw/mediatools"
$FfmpegVersion = "9.0.2"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType File -Force -Path (Join-Path $OutDir ".gitkeep") | Out-Null

$Temp = Join-Path ([System.IO.Path]::GetTempPath()) ("anote-mediatools-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $Temp | Out-Null

function Get-Url([string]$Url, [string]$OutFile) {
    Write-Host "Downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
}

try {
    $map = @(
        @{ Abi = "arm64-v8a"; FfmpegArch = "arm64"; YtDlp = "yt-dlp_linux_aarch64" },
        @{ Abi = "armeabi-v7a"; FfmpegArch = "arm"; YtDlp = "yt-dlp_linux_armv7l" },
        @{ Abi = "x86_64"; FfmpegArch = "x64"; YtDlp = "yt-dlp_linux" }
    )

    foreach ($item in $map) {
        $zip = Join-Path $Temp ("ffmpeg-" + $item.FfmpegArch + ".zip")
        $url = "https://github.com/Tyrrrz/FFmpegBin/releases/download/$FfmpegVersion/ffmpeg-android-$($item.FfmpegArch).zip"
        Get-Url $url $zip

        $extract = Join-Path $Temp ("ffmpeg-" + $item.FfmpegArch)
        if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
        Expand-Archive -Path $zip -DestinationPath $extract -Force

        $binary = Get-ChildItem -Path $extract -Recurse -File | Where-Object { $_.Name -ieq "ffmpeg" } | Select-Object -First 1
        if (-not $binary) { throw "ffmpeg binary not found in $zip" }
        $dest = Join-Path $OutDir ("ffmpeg." + $item.Abi)
        Copy-Item -Force $binary.FullName $dest
        Write-Host "Wrote $dest ($([math]::Round((Get-Item $dest).Length / 1MB, 1)) MB)"

        $ytUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/$($item.YtDlp)"
        $ytDest = Join-Path $OutDir ("yt-dlp." + $item.Abi)
        Get-Url $ytUrl $ytDest
        Write-Host "Wrote $ytDest ($([math]::Round((Get-Item $ytDest).Length / 1MB, 1)) MB)"
    }

    Write-Host "Media tools assets ready in $OutDir"
}
finally {
    if (Test-Path $Temp) { Remove-Item -Recurse -Force $Temp }
}
