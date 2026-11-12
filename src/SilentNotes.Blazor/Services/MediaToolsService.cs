// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using SilentNotes.Workers;

#if ANDROID
using Android.OS;
using Android.Systems;
#endif

namespace SilentNotes.Services
{
    /// <summary>
    /// Installs ffmpeg / yt-dlp from Maui assets (shipped in the APK) or downloads updates from
    /// GitHub releases. Executables live under AppData/mediatools (chmod +x on Android); mirrored
    /// copies are kept in <see cref="AnotePaths.FfmpegDir"/> and <see cref="AnotePaths.YtDlpDir"/>.
    /// </summary>
    public class MediaToolsService : IMediaToolsService
    {
        /// <summary>Pinned FFmpegBin release used when bundling / first download.</summary>
        public const string BundledFfmpegBinVersion = "9.0.2";

        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        private readonly object _gate = new object();
        private bool _busy;

        /// <inheritdoc/>
        public string FfmpegPath { get; private set; }

        /// <inheritdoc/>
        public string YtDlpPath { get; private set; }

        /// <inheritdoc/>
        public string FfmpegStatus { get; private set; } = "…";

        /// <inheritdoc/>
        public string YtDlpStatus { get; private set; } = "…";

        /// <inheritdoc/>
        public bool IsBusy
        {
            get { lock (_gate) return _busy; }
        }

        /// <summary>App-private directory where binaries are executed from.</summary>
        public static string ExecDirectory =>
            Path.Combine(FileSystem.AppDataDirectory, "mediatools");

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaToolsService"/> class.
        /// </summary>
        public MediaToolsService()
        {
            EnsureReady();
        }

        /// <inheritdoc/>
        public void EnsureReady()
        {
            AnotePaths.EnsureDirectories();
            try
            {
                Directory.CreateDirectory(ExecDirectory);
            }
            catch (Exception)
            {
            }

            RefreshPaths();
        }

        /// <inheritdoc/>
        public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
        {
            if (!TryBeginBusy())
                return;

            try
            {
                AnotePaths.EnsureDirectories();
                Directory.CreateDirectory(ExecDirectory);

                await EnsureToolAsync(
                    toolName: "ffmpeg",
                    assetFileName: "ffmpeg." + GetDeviceAbi(),
                    publicDir: AnotePaths.FfmpegDir,
                    allowDownloadIfMissing: true,
                    forceDownload: false,
                    cancellationToken).ConfigureAwait(false);

                await EnsureToolAsync(
                    toolName: "yt-dlp",
                    assetFileName: "yt-dlp." + GetDeviceAbi(),
                    publicDir: AnotePaths.YtDlpDir,
                    allowDownloadIfMissing: true,
                    forceDownload: false,
                    cancellationToken).ConfigureAwait(false);

                RefreshPaths();
            }
            finally
            {
                EndBusy();
                RefreshPaths();
            }
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateFfmpegAsync(CancellationToken cancellationToken = default)
        {
            if (!TryBeginBusy())
                return false;

            try
            {
                FfmpegStatus = "Updating…";
                bool ok = await EnsureToolAsync(
                    toolName: "ffmpeg",
                    assetFileName: "ffmpeg." + GetDeviceAbi(),
                    publicDir: AnotePaths.FfmpegDir,
                    allowDownloadIfMissing: true,
                    forceDownload: true,
                    cancellationToken).ConfigureAwait(false);
                RefreshPaths();
                if (!ok || string.IsNullOrEmpty(FfmpegPath))
                    AnoteFileLog.Error("Ffmpeg", "ffmpeg update failed");
                return ok && !string.IsNullOrEmpty(FfmpegPath);
            }
            finally
            {
                EndBusy();
                RefreshPaths();
            }
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateYtDlpAsync(CancellationToken cancellationToken = default)
        {
            if (!TryBeginBusy())
                return false;

            try
            {
                YtDlpStatus = "Updating…";
                bool ok = await EnsureToolAsync(
                    toolName: "yt-dlp",
                    assetFileName: "yt-dlp." + GetDeviceAbi(),
                    publicDir: AnotePaths.YtDlpDir,
                    allowDownloadIfMissing: true,
                    forceDownload: true,
                    cancellationToken).ConfigureAwait(false);
                RefreshPaths();
                if (!ok || string.IsNullOrEmpty(YtDlpPath))
                    AnoteFileLog.Error("YtDlp", "yt-dlp update failed");
                return ok && !string.IsNullOrEmpty(YtDlpPath);
            }
            finally
            {
                EndBusy();
                RefreshPaths();
            }
        }

        private async Task<bool> EnsureToolAsync(
            string toolName,
            string assetFileName,
            string publicDir,
            bool allowDownloadIfMissing,
            bool forceDownload,
            CancellationToken cancellationToken)
        {
            string execPath = Path.Combine(ExecDirectory, toolName);
            string publicPath = Path.Combine(publicDir, toolName);

            if (!forceDownload && File.Exists(execPath) && new FileInfo(execPath).Length > 1024)
            {
                // Android: discard leftover Linux ELF yt-dlp downloads and refresh the Python script.
                if (string.Equals(toolName, "yt-dlp", StringComparison.OrdinalIgnoreCase)
                    && LooksLikeElf(execPath))
                {
                    TryDelete(execPath);
                }
                else
                {
                    MakeExecutable(execPath);
                    MirrorToPublic(execPath, publicPath);
                    return true;
                }
            }

            // 1) Bundled Maui asset (shipped in APK)
            if (!forceDownload && await TryInstallFromAssetAsync(assetFileName, execPath, cancellationToken).ConfigureAwait(false))
            {
                MirrorToPublic(execPath, publicPath);
                return true;
            }

            // 2) Download from GitHub
            if (!allowDownloadIfMissing && !forceDownload)
                return File.Exists(execPath);

            try
            {
                if (string.Equals(toolName, "ffmpeg", StringComparison.OrdinalIgnoreCase))
                    await DownloadFfmpegAsync(execPath, cancellationToken).ConfigureAwait(false);
                else
                    await DownloadYtDlpAsync(execPath, cancellationToken).ConfigureAwait(false);

                MakeExecutable(execPath);
                MirrorToPublic(execPath, publicPath);
                return File.Exists(execPath);
            }
            catch (Exception ex)
            {
                string page = string.Equals(toolName, "ffmpeg", StringComparison.OrdinalIgnoreCase) ? "Ffmpeg" : "YtDlp";
                AnoteFileLog.Error(page, toolName + " install failed", ex);
                return File.Exists(execPath);
            }
        }

        private static async Task<bool> TryInstallFromAssetAsync(
            string assetFileName,
            string execPath,
            CancellationToken cancellationToken)
        {
            try
            {
                string assetPath = "mediatools/" + assetFileName;
                await using Stream stream = await FileSystem.OpenAppPackageFileAsync(assetPath).ConfigureAwait(false);
                if (stream == null)
                    return false;

                string temp = execPath + ".tmp";
                await using (FileStream output = File.Create(temp))
                {
                    await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                ReplaceFile(temp, execPath);
                MakeExecutable(execPath);
                return File.Exists(execPath) && new FileInfo(execPath).Length > 1024;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async Task DownloadFfmpegAsync(string execPath, CancellationToken cancellationToken)
        {
            string abi = GetDeviceAbi();
            string arch = AbiToFfmpegArch(abi);
            string url = "https://github.com/Tyrrrz/FFmpegBin/releases/download/"
                + BundledFfmpegBinVersion
                + "/ffmpeg-android-" + arch + ".zip";

            string tempZip = execPath + ".zip";
            string tempExtract = execPath + ".extract";
            try
            {
                await DownloadToFileAsync(url, tempZip, cancellationToken).ConfigureAwait(false);
                if (Directory.Exists(tempExtract))
                    Directory.Delete(tempExtract, true);
                Directory.CreateDirectory(tempExtract);
                ZipFile.ExtractToDirectory(tempZip, tempExtract);

                string found = FindExtractedBinary(tempExtract, "ffmpeg");
                if (found == null)
                    throw new InvalidOperationException("ffmpeg binary missing from zip.");

                string temp = execPath + ".tmp";
                File.Copy(found, temp, true);
                ReplaceFile(temp, execPath);
            }
            finally
            {
                TryDelete(tempZip);
                TryDeleteDir(tempExtract);
            }
        }

        private static async Task DownloadYtDlpAsync(string execPath, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            foreach (string asset in YtDlpAssetsForAbi(GetDeviceAbi()))
            {
                string url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/" + asset;
                string temp = execPath + ".tmp";
                string tempZip = execPath + ".zip";
                string tempExtract = execPath + ".extract";
                try
                {
                    if (asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        await DownloadToFileAsync(url, tempZip, cancellationToken).ConfigureAwait(false);
                        if (Directory.Exists(tempExtract))
                            Directory.Delete(tempExtract, true);
                        Directory.CreateDirectory(tempExtract);
                        ZipFile.ExtractToDirectory(tempZip, tempExtract);
                        string found = FindExtractedBinary(tempExtract, "yt-dlp")
                            ?? FindExtractedBinaryPrefix(tempExtract, "yt-dlp");
                        if (found == null)
                            throw new InvalidOperationException("yt-dlp binary missing from " + asset);
                        File.Copy(found, temp, true);
                    }
                    else
                    {
                        await DownloadToFileAsync(url, temp, cancellationToken).ConfigureAwait(false);
                    }

                    ReplaceFile(temp, execPath);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                finally
                {
                    TryDelete(temp);
                    TryDelete(tempZip);
                    TryDeleteDir(tempExtract);
                }
            }

            throw lastError ?? new InvalidOperationException("Could not download yt-dlp for this ABI.");
        }

        private static string FindExtractedBinaryPrefix(string root, string namePrefix)
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(file);
                if (fileName != null
                    && fileName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)
                    && !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }

        private static async Task DownloadToFileAsync(string url, string path, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream output = File.Create(path);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        private static string FindExtractedBinary(string root, string name)
        {
            string direct = Path.Combine(root, name);
            if (File.Exists(direct))
                return direct;

            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }

        private static void MirrorToPublic(string execPath, string publicPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(publicPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(execPath, publicPath, true);
                MakeExecutable(publicPath);
            }
            catch (Exception)
            {
                // /sdcard may be noexec or unwritable; execution still uses AppData copy.
            }
        }

        private static void ReplaceFile(string tempPath, string finalPath)
        {
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);
        }

        private static void MakeExecutable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;
#if ANDROID
            try
            {
                Os.Chmod(path, Convert.ToInt32("755", 8));
            }
            catch (Exception)
            {
                try
                {
                    // Fallback for older bindings / SELinux edge cases
                    Java.Lang.Runtime.GetRuntime()?.Exec(new[] { "chmod", "755", path })?.WaitFor();
                }
                catch (Exception)
                {
                }
            }
#endif
        }

        private void RefreshPaths()
        {
            string ffmpegExec = Path.Combine(ExecDirectory, "ffmpeg");
            string ytExec = Path.Combine(ExecDirectory, "yt-dlp");

            FfmpegPath = File.Exists(ffmpegExec)
                ? ffmpegExec
                : AnotePaths.ResolveFfmpegBinary();
            YtDlpPath = File.Exists(ytExec)
                ? ytExec
                : AnotePaths.ResolveYtDlpBinary();

            FfmpegStatus = Describe(FfmpegPath, AnotePaths.FfmpegDir);
            YtDlpStatus = Describe(YtDlpPath, AnotePaths.YtDlpDir);
        }

        private static string Describe(string path, string publicDir)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "Not installed — tap Update, or place a binary in " + publicDir;

            try
            {
                var info = new FileInfo(path);
                string mb = (info.Length / (1024.0 * 1024.0)).ToString("0.0");
                return "Installed (" + mb + " MB) — " + path;
            }
            catch (Exception)
            {
                return "Installed — " + path;
            }
        }

        private bool TryBeginBusy()
        {
            lock (_gate)
            {
                if (_busy)
                    return false;
                _busy = true;
                return true;
            }
        }

        private void EndBusy()
        {
            lock (_gate)
            {
                _busy = false;
            }
        }

        private static string GetDeviceAbi()
        {
#if ANDROID
            try
            {
                var abis = Build.SupportedAbis;
                if (abis != null)
                {
                    foreach (string abi in abis)
                    {
                        if (abi == "arm64-v8a" || abi == "armeabi-v7a" || abi == "x86_64" || abi == "x86")
                            return abi;
                    }

                    if (abis.Count > 0 && !string.IsNullOrEmpty(abis[0]))
                        return abis[0];
                }
            }
            catch (Exception)
            {
            }

            return "arm64-v8a";
#else
            return "x86_64";
#endif
        }

        private static string AbiToFfmpegArch(string abi) => abi switch
        {
            "armeabi-v7a" => "arm",
            "arm64-v8a" => "arm64",
            "x86" => "x86",
            "x86_64" => "x64",
            _ => "arm64",
        };

        /// <summary>
        /// Android cannot run official yt-dlp Linux ELFs (glibc). Always fetch the universal
        /// Python zipapp named <c>yt-dlp</c> and execute it with the bundled Android Python.
        /// </summary>
        private static string[] YtDlpAssetsForAbi(string abi) => new[] { "yt-dlp" };

        private static bool LooksLikeElf(string path)
        {
            try
            {
                using FileStream fs = File.OpenRead(path);
                return fs.ReadByte() == 0x7F
                    && fs.ReadByte() == (byte)'E'
                    && fs.ReadByte() == (byte)'L'
                    && fs.ReadByte() == (byte)'F';
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        private static void TryDeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception)
            {
            }
        }
    }
}
