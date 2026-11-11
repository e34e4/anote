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

function Try-Download([string]$Url, [string]$OutFile) {
    Write-Host "Downloading $Url"
    try {
        Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing -MaximumRedirection 5
        if (-not (Test-Path $OutFile) -or ((Get-Item $OutFile).Length -lt 1024)) {
            Write-Warning "Download too small or missing: $Url"
            return $false
        }
        $head = Get-Content -Path $OutFile -TotalCount 1 -ErrorAction SilentlyContinue
        if ($head -is [string] -and ($head.StartsWith("<!") -or $head.StartsWith("<html"))) {
            Write-Warning "Download looks like an HTML error page: $Url"
            Remove-Item -Force $OutFile -ErrorAction SilentlyContinue
            return $false
        }
        return $true
    }
    catch {
        Write-Warning "Download failed: $Url — $($_.Exception.Message)"
        return $false
    }
}

function Save-YtDlpAsset([string]$AssetName, [string]$DestPath, [string]$WorkDir) {
    $url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/$AssetName"
    $download = Join-Path $WorkDir ("dl-" + $AssetName.Replace("/", "_"))
    if (-not (Try-Download $url $download)) {
        return $false
    }

    if ($AssetName.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
        $extract = Join-Path $WorkDir ("ex-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force -Path $extract | Out-Null
        Expand-Archive -Path $download -DestinationPath $extract -Force
        $binary = Get-ChildItem -Path $extract -Recurse -File |
            Where-Object { $_.Name -ieq "yt-dlp" -or $_.Name -like "yt-dlp_linux*" } |
            Sort-Object { if ($_.Name -ieq "yt-dlp") { 0 } else { 1 } } |
            Select-Object -First 1
        if (-not $binary) {
            Write-Warning "No yt-dlp binary inside $AssetName"
            return $false
        }
        Copy-Item -Force $binary.FullName $DestPath
    }
    else {
        Copy-Item -Force $download $DestPath
    }
    return $true
}

try {
    $map = @(
        @{ Abi = "arm64-v8a"; FfmpegArch = "arm64"; YtDlpCandidates = @("yt-dlp_linux_aarch64", "yt-dlp_linux_aarch64.zip", "yt-dlp") },
        @{ Abi = "armeabi-v7a"; FfmpegArch = "arm"; YtDlpCandidates = @("yt-dlp_linux_armv7l.zip", "yt-dlp_linux_armv7l", "yt-dlp") },
        @{ Abi = "x86_64"; FfmpegArch = "x64"; YtDlpCandidates = @("yt-dlp_linux", "yt-dlp_linux.zip", "yt-dlp") }
    )

    foreach ($item in $map) {
        $zip = Join-Path $Temp ("ffmpeg-" + $item.FfmpegArch + ".zip")
        $url = "https://github.com/Tyrrrz/FFmpegBin/releases/download/$FfmpegVersion/ffmpeg-android-$($item.FfmpegArch).zip"
        if (-not (Try-Download $url $zip)) {
            throw "Required ffmpeg download failed for ABI $($item.Abi)"
        }

        $extract = Join-Path $Temp ("ffmpeg-" + $item.FfmpegArch)
        if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
        Expand-Archive -Path $zip -DestinationPath $extract -Force

        $binary = Get-ChildItem -Path $extract -Recurse -File | Where-Object { $_.Name -ieq "ffmpeg" } | Select-Object -First 1
        if (-not $binary) { throw "ffmpeg binary not found in $zip" }
        $dest = Join-Path $OutDir ("ffmpeg." + $item.Abi)
        Copy-Item -Force $binary.FullName $dest
        Write-Host "Wrote $dest ($([math]::Round((Get-Item $dest).Length / 1MB, 1)) MB)"

        $ytDest = Join-Path $OutDir ("yt-dlp." + $item.Abi)
        $ytOk = $false
        foreach ($asset in $item.YtDlpCandidates) {
            $work = Join-Path $Temp ("yt-" + $item.Abi + "-" + $asset.Replace(".", "_"))
            New-Item -ItemType Directory -Force -Path $work | Out-Null
            if (Save-YtDlpAsset $asset $ytDest $work) {
                Write-Host "Wrote $ytDest from $asset ($([math]::Round((Get-Item $ytDest).Length / 1MB, 1)) MB)"
                $ytOk = $true
                break
            }
        }
        if (-not $ytOk) {
            Write-Warning "No yt-dlp asset available for ABI $($item.Abi); runtime Update can still fetch later."
        }
    }

    Write-Host "Media tools assets ready in $OutDir"
    Get-ChildItem $OutDir | Format-Table Name, Length
}
finally {
    if (Test-Path $Temp) { Remove-Item -Recurse -Force $Temp }
}
