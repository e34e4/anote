// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Systems;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Runs yt-dlp on Android via the bundled Python interpreter from youtubedl-android
    /// (<c>libpython.so</c> + <c>libpython.zip.so</c>). Zip extraction must recreate Unix
    /// symlinks (zlib etc.); plain <see cref="ZipFile.ExtractToDirectory"/> leaves 13-byte stubs.
    /// </summary>
    internal static class AnoteYtDlpRuntime
    {
        private const string BaseName = "youtubedl-android";
        private const string PackagesRoot = "packages";
        private const string PythonDirName = "python";
        private const string FfmpegDirName = "ffmpeg";
        private const string PythonBinName = "libpython.so";
        private const string PythonZipName = "libpython.zip.so";
        private const string FfmpegBinName = "libffmpeg.so";
        private const string FfmpegZipName = "libffmpeg.zip.so";
        /// <summary>v2 = symlink-aware extract (fixes zlib not available).</summary>
        private const string MarkerFileName = ".runtime_ready_v2";

        private static readonly object Gate = new object();
        private static bool _ready;

        public static bool IsPythonAvailable(Context context)
        {
            try
            {
                string native = context?.ApplicationInfo?.NativeLibraryDir;
                if (string.IsNullOrEmpty(native))
                    return false;
                return File.Exists(Path.Combine(native, PythonBinName))
                    && File.Exists(Path.Combine(native, PythonZipName));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Absolute path to Android-native libffmpeg.so when packaged, else null.</summary>
        public static string ResolvePackagedFfmpeg(Context context)
        {
            try
            {
                string native = context?.ApplicationInfo?.NativeLibraryDir;
                if (string.IsNullOrEmpty(native))
                    return null;
                string path = Path.Combine(native, FfmpegBinName);
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void EnsureReady(Context context)
        {
            if (context == null)
                throw new InvalidOperationException("Context required for yt-dlp runtime.");

            lock (Gate)
            {
                if (_ready && IsRuntimeTreeReady(context))
                    return;

                string native = context.ApplicationInfo?.NativeLibraryDir
                    ?? throw new InvalidOperationException("NativeLibraryDir missing.");
                string pythonBin = Path.Combine(native, PythonBinName);
                string pythonZip = Path.Combine(native, PythonZipName);
                if (!File.Exists(pythonBin) || !File.Exists(pythonZip))
                {
                    throw new InvalidOperationException(
                        "Android Python runtime missing (libpython.so). Reinstall the app / rebuild with youtubedl-android.");
                }

                Java.IO.File noBackup = context.NoBackupFilesDir;
                string baseDir = Path.Combine(noBackup.AbsolutePath, BaseName);
                string packagesDir = Path.Combine(baseDir, PackagesRoot);
                string pythonDir = Path.Combine(packagesDir, PythonDirName);
                string ffmpegDir = Path.Combine(packagesDir, FfmpegDirName);
                string marker = Path.Combine(packagesDir, MarkerFileName);

                long pythonZipSize = new FileInfo(pythonZip).Length;
                string ffmpegZip = Path.Combine(native, FfmpegZipName);
                long ffmpegZipSize = File.Exists(ffmpegZip) ? new FileInfo(ffmpegZip).Length : 0;
                string expectedMarker = pythonZipSize + ":" + ffmpegZipSize;

                bool needExtract = !File.Exists(marker)
                    || !Directory.Exists(Path.Combine(pythonDir, "usr", "lib"))
                    || !string.Equals(SafeRead(marker), expectedMarker, StringComparison.Ordinal)
                    || !SymlinksLookHealthy(pythonDir);

                if (needExtract)
                {
                    AnoteFileLog.Append("ytdlp", "Extracting Android Python/ffmpeg packages (symlink-aware)…");
                    TryDeleteDir(pythonDir);
                    TryDeleteDir(ffmpegDir);
                    Directory.CreateDirectory(pythonDir);
                    ExtractZipPreservingSymlinks(pythonZip, pythonDir);

                    if (File.Exists(ffmpegZip) && ffmpegZipSize > 1024)
                    {
                        Directory.CreateDirectory(ffmpegDir);
                        ExtractZipPreservingSymlinks(ffmpegZip, ffmpegDir);
                    }

                    Directory.CreateDirectory(packagesDir);
                    File.WriteAllText(marker, expectedMarker);
                    AnoteFileLog.Append("ytdlp", "Extracted Android Python runtime to " + pythonDir
                        + (Directory.Exists(ffmpegDir) ? " (+ ffmpeg libs)" : string.Empty));
                }

                _ready = true;
            }
        }

        public static Java.Lang.Process Start(
            Context context,
            string ytDlpScriptPath,
            IReadOnlyList<string> ytDlpArguments)
        {
            EnsureReady(context);

            string native = context.ApplicationInfo.NativeLibraryDir;
            string pythonBin = Path.Combine(native, PythonBinName);
            Java.IO.File noBackup = context.NoBackupFilesDir;
            string packagesDir = Path.Combine(noBackup.AbsolutePath, BaseName, PackagesRoot);
            string pythonDir = Path.Combine(packagesDir, PythonDirName);
            string ffmpegDir = Path.Combine(packagesDir, FfmpegDirName);
            string pythonHome = Path.Combine(pythonDir, "usr");
            string pythonLib = Path.Combine(pythonHome, "lib");
            string ffmpegLib = Path.Combine(ffmpegDir, "usr", "lib");
            string cert = Path.Combine(pythonHome, "etc", "tls", "cert.pem");

            if (string.IsNullOrEmpty(ytDlpScriptPath) || !File.Exists(ytDlpScriptPath))
                throw new FileNotFoundException("yt-dlp script not found", ytDlpScriptPath);

            if (LooksLikeElf(ytDlpScriptPath))
            {
                throw new InvalidOperationException(
                    "Installed yt-dlp is a Linux binary, not the Python script. Use Settings → Update yt-dlp.");
            }

            var cmd = new List<string> { pythonBin, ytDlpScriptPath };
            if (ytDlpArguments != null)
                cmd.AddRange(ytDlpArguments);

            var builder = new Java.Lang.ProcessBuilder(cmd.ToArray());
            var env = builder.Environment();
            var ldParts = new List<string> { pythonLib, native };
            if (Directory.Exists(ffmpegLib))
                ldParts.Insert(1, ffmpegLib);
            env["LD_LIBRARY_PATH"] = string.Join(":", ldParts);
            if (File.Exists(cert))
                env["SSL_CERT_FILE"] = cert;
            env["PYTHONHOME"] = pythonHome;
            env["HOME"] = pythonHome;
            env["TMPDIR"] = context.CacheDir?.AbsolutePath ?? Path.GetTempPath();
            string pathEnv = Java.Lang.JavaSystem.Getenv("PATH") ?? "/system/bin";
            env["PATH"] = pathEnv + ":" + native;

            AnoteFileLog.Append("ytdlp", "python-exec: " + string.Join(' ', cmd));
            return builder.Start();
        }

        public static async Task<(int ExitCode, string Output, string LastPath)> RunAsync(
            Context context,
            string ytDlpScriptPath,
            IReadOnlyList<string> ytDlpArguments,
            IProgress<(double? Percent, string Status)> progress,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var output = new StringBuilder();
                string lastPath = null;
                Java.Lang.Process process = null;
                try
                {
                    process = Start(context, ytDlpScriptPath, ytDlpArguments);
                    using (cancellationToken.Register(() =>
                    {
                        try { process?.Destroy(); } catch (Exception) { }
                    }))
                    {
                        void ReadStream(Stream stream)
                        {
                            if (stream == null)
                                return;
                            using var reader = new StreamReader(stream);
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                lock (output)
                                    output.AppendLine(line);

                                var m = System.Text.RegularExpressions.Regex.Match(
                                    line, @"\[download\]\s+(\d{1,3}(?:\.\d+)?)%");
                                if (m.Success
                                    && double.TryParse(m.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out double pct))
                                {
                                    progress?.Report((Math.Clamp(pct, 0, 100), line.Trim()));
                                }
                                else if (line.IndexOf("[download] Destination:", StringComparison.OrdinalIgnoreCase) >= 0
                                    || line.IndexOf("Merging formats into", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    int q1 = line.IndexOf('"');
                                    int q2 = line.LastIndexOf('"');
                                    if (q1 >= 0 && q2 > q1)
                                        lastPath = line.Substring(q1 + 1, q2 - q1 - 1);
                                    progress?.Report((null, Truncate(line.Trim(), 120)));
                                }
                                else if (line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase)
                                    || line.StartsWith("[info]", StringComparison.OrdinalIgnoreCase)
                                    || line.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase))
                                {
                                    progress?.Report((null, Truncate(line.Trim(), 120)));
                                }
                            }
                        }

                        Task stdoutTask = Task.Run(() => ReadStream(process.InputStream), cancellationToken);
                        Task stderrTask = Task.Run(() => ReadStream(process.ErrorStream), cancellationToken);
                        Task.WaitAll(new[] { stdoutTask, stderrTask }, cancellationToken);
                        int code = process.WaitFor();
                        string text;
                        lock (output)
                            text = output.ToString();
                        return (code, text, lastPath);
                    }
                }
                finally
                {
                    try { process?.Destroy(); } catch (Exception) { }
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Like youtubedl-android ZipUtils.unzip: recreate Unix symlinks with <see cref="Os.Symlink"/>
        /// instead of writing the target path as a tiny regular file.
        /// </summary>
        private static void ExtractZipPreservingSymlinks(string zipPath, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);
            string targetRoot = Path.GetFullPath(targetDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(name) || name.EndsWith("/", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(name))
                        Directory.CreateDirectory(Path.Combine(targetDirectory, name));
                    continue;
                }

                string destPath = Path.GetFullPath(Path.Combine(targetDirectory, name));
                if (!destPath.StartsWith(targetRoot, StringComparison.Ordinal))
                    throw new InvalidOperationException("Zip entry outside target: " + name);

                string parent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                if (IsUnixSymlink(entry))
                {
                    string linkTarget;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        linkTarget = reader.ReadToEnd();

                    try
                    {
                        if (File.Exists(destPath) || Directory.Exists(destPath) || IsSymlinkPath(destPath))
                            File.Delete(destPath);
                    }
                    catch (Exception)
                    {
                        try { Directory.Delete(destPath); } catch (Exception) { }
                    }

                    Os.Symlink(linkTarget, destPath);
                }
                else
                {
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }

        private static bool IsUnixSymlink(ZipArchiveEntry entry)
        {
            // Upper 16 bits of external attributes are the Unix mode when create_system is Unix.
            int mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
            const int sIfLnk = 0xA000;
            return (mode & 0xF000) == sIfLnk;
        }

        private static bool SymlinksLookHealthy(string pythonDir)
        {
            try
            {
                string libz = Path.Combine(pythonDir, "usr", "lib", "libz.so.1");
                if (!File.Exists(libz) && !IsSymlinkPath(libz))
                    return false;
                // Broken extract wrote the target name as a 13-byte regular file.
                var info = new FileInfo(libz);
                if (info.Exists && info.Length > 0 && info.Length < 64 && !IsSymlinkPath(libz))
                    return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsSymlinkPath(string path)
        {
            try
            {
                return Os.Readlink(path) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsRuntimeTreeReady(Context context)
        {
            try
            {
                Java.IO.File noBackup = context.NoBackupFilesDir;
                string packagesDir = Path.Combine(noBackup.AbsolutePath, BaseName, PackagesRoot);
                string marker = Path.Combine(packagesDir, MarkerFileName);
                string pythonDir = Path.Combine(packagesDir, PythonDirName);
                return File.Exists(marker) && SymlinksLookHealthy(pythonDir);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool LooksLikeElf(string path)
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

        private static string SafeRead(string path)
        {
            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch (Exception)
            {
                return string.Empty;
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

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;
            return text.Substring(0, max) + "…";
        }
    }
}
