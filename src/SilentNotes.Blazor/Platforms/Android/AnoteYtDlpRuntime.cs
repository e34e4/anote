// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Runs yt-dlp on Android via the bundled Python interpreter from youtubedl-android
    /// (<c>libpython.so</c> in the native library directory). Official yt-dlp Linux binaries are
    /// glibc ELFs and cannot run on Android (linker rejects them / wrong libc).
    /// </summary>
    internal static class AnoteYtDlpRuntime
    {
        private const string BaseName = "youtubedl-android";
        private const string PackagesRoot = "packages";
        private const string PythonDirName = "python";
        private const string PythonBinName = "libpython.so";
        private const string PythonZipName = "libpython.zip.so";
        private const string FfmpegBinName = "libffmpeg.so";
        private const string MarkerFileName = ".python_ready";

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
                if (_ready && IsPythonTreeReady(context))
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
                string pythonDir = Path.Combine(baseDir, PackagesRoot, PythonDirName);
                string marker = Path.Combine(pythonDir, MarkerFileName);
                long zipSize = new FileInfo(pythonZip).Length;

                bool needExtract = !File.Exists(marker)
                    || !Directory.Exists(Path.Combine(pythonDir, "usr"))
                    || !string.Equals(File.ReadAllText(marker).Trim(), zipSize.ToString(), StringComparison.Ordinal);

                if (needExtract)
                {
                    if (Directory.Exists(pythonDir))
                        Directory.Delete(pythonDir, true);
                    Directory.CreateDirectory(pythonDir);
                    ZipFile.ExtractToDirectory(pythonZip, pythonDir);
                    File.WriteAllText(marker, zipSize.ToString());
                    AnoteFileLog.Append("ytdlp", "Extracted Android Python runtime to " + pythonDir);
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
            string pythonDir = Path.Combine(noBackup.AbsolutePath, BaseName, PackagesRoot, PythonDirName);
            string pythonHome = Path.Combine(pythonDir, "usr");
            string pythonLib = Path.Combine(pythonHome, "lib");
            string cert = Path.Combine(pythonHome, "etc", "tls", "cert.pem");

            if (string.IsNullOrEmpty(ytDlpScriptPath) || !File.Exists(ytDlpScriptPath))
                throw new FileNotFoundException("yt-dlp script not found", ytDlpScriptPath);

            // Reject leftover Linux ELF "yt-dlp" downloads — they cannot run on Android.
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
            string ld = pythonLib;
            string packagedFfmpegDir = native;
            env["LD_LIBRARY_PATH"] = ld + ":" + packagedFfmpegDir;
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
                var output = new System.Text.StringBuilder();
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

        private static bool IsPythonTreeReady(Context context)
        {
            try
            {
                Java.IO.File noBackup = context.NoBackupFilesDir;
                string marker = Path.Combine(
                    noBackup.AbsolutePath, BaseName, PackagesRoot, PythonDirName, MarkerFileName);
                return File.Exists(marker);
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

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;
            return text.Substring(0, max) + "…";
        }
    }
}
