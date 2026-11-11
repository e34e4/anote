// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Executes the installed yt-dlp binary (preferring app-private mediatools copy) and streams
    /// progress from stdout/stderr. Failures are appended under <see cref="AnotePaths.LogDir"/>.
    /// </summary>
    internal sealed class YtDlpDownloadService : IYtDlpDownloadService
    {
        private static readonly Regex PercentRegex = new Regex(
            @"\[download\]\s+(\d{1,3}(?:\.\d+)?)%",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly IMediaToolsService _mediaTools;

        public YtDlpDownloadService(IMediaToolsService mediaTools)
        {
            _mediaTools = mediaTools ?? throw new ArgumentNullException(nameof(mediaTools));
        }

        /// <inheritdoc/>
        public bool IsAvailable
        {
            get
            {
                _mediaTools.EnsureReady();
                return !string.IsNullOrEmpty(ResolveYtDlpPath());
            }
        }

        /// <inheritdoc/>
        public async Task<YtDlpDownloadResult> DownloadAsync(
            string url,
            YtDlpQualityChoice choice,
            IProgress<YtDlpDownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Fail("URL is empty");
            if (choice == null)
                return Fail("Quality choice is required");

            try
            {
                await _mediaTools.EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "EnsureInstalled failed: " + ex);
                return Fail("Could not prepare yt-dlp: " + ex.Message);
            }

            string ytDlp = ResolveYtDlpPath();
            if (string.IsNullOrEmpty(ytDlp))
                return Fail("yt-dlp not found. Use Settings → Update yt-dlp.");

            AnotePaths.EnsureDirectories();
            string outDir = AnotePaths.Down;
            try
            {
                Directory.CreateDirectory(outDir);
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "Create down dir failed: " + ex);
                return Fail("Cannot write to " + outDir + ": " + ex.Message);
            }

            string ffmpeg = ResolveFfmpegPath();
            var args = new List<string>(choice.BuildArguments(outDir, ffmpeg)) { url.Trim() };

            progress?.Report(new YtDlpDownloadProgress { Percent = 0, StatusText = "Starting yt-dlp…" });

            try
            {
                (int code, string output, string lastPath) = await RunProcessAsync(
                    ytDlp, args, progress, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                    return Fail("Cancelled");

                if (code != 0)
                {
                    string detail = Truncate(output, 2000);
                    AnoteFileLog.Append("ytdlp", "exit=" + code + " url=" + url + "\n" + detail);
                    return Fail(string.IsNullOrWhiteSpace(detail)
                        ? "yt-dlp failed (exit " + code + ")"
                        : "yt-dlp failed: " + FirstUsefulLine(detail));
                }

                string path = lastPath;
                if (string.IsNullOrEmpty(path))
                    path = TryFindNewestFile(outDir);

                progress?.Report(new YtDlpDownloadProgress { Percent = 100, StatusText = "Done" });
                return new YtDlpDownloadResult
                {
                    Success = true,
                    OutputPath = path,
                    Message = string.IsNullOrEmpty(path)
                        ? "Downloaded to " + outDir
                        : "Saved: " + Path.GetFileName(path),
                };
            }
            catch (OperationCanceledException)
            {
                return Fail("Cancelled");
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "Download exception for " + url + ": " + ex);
                return Fail(ex.Message);
            }
        }

        private string ResolveYtDlpPath()
        {
            if (!string.IsNullOrEmpty(_mediaTools.YtDlpPath) && File.Exists(_mediaTools.YtDlpPath))
                return _mediaTools.YtDlpPath;
            return AnotePaths.ResolveYtDlpBinary();
        }

        private string ResolveFfmpegPath()
        {
            if (!string.IsNullOrEmpty(_mediaTools.FfmpegPath) && File.Exists(_mediaTools.FfmpegPath))
                return _mediaTools.FfmpegPath;
            return AnotePaths.ResolveFfmpegBinary();
        }

        private static async Task<(int ExitCode, string Output, string LastPath)> RunProcessAsync(
            string binary,
            IReadOnlyList<string> arguments,
            IProgress<YtDlpDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var cmd = new List<string> { binary };
            if (arguments != null)
                cmd.AddRange(arguments);

            return await Task.Run(() =>
            {
                var output = new StringBuilder();
                string lastPath = null;
                Java.Lang.Process process = null;
                try
                {
                    process = Java.Lang.Runtime.GetRuntime().Exec(cmd.ToArray());
                    using (cancellationToken.Register(() =>
                    {
                        try { process?.Destroy(); } catch (Exception) { }
                    }))
                    {
                        void ReadJavaStream(Java.IO.InputStream stream)
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

                                Match m = PercentRegex.Match(line);
                                if (m.Success
                                    && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                                {
                                    progress?.Report(new YtDlpDownloadProgress
                                    {
                                        Percent = Math.Clamp(pct, 0, 100),
                                        StatusText = line.Trim(),
                                    });
                                }
                                else if (line.IndexOf("[download] Destination:", StringComparison.OrdinalIgnoreCase) >= 0
                                    || line.IndexOf("[Extractor] Destination:", StringComparison.OrdinalIgnoreCase) >= 0
                                    || line.IndexOf("Merging formats into", StringComparison.OrdinalIgnoreCase) >= 0
                                    || line.IndexOf("[FixupM3u8]", StringComparison.OrdinalIgnoreCase) >= 0
                                    || line.IndexOf("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    string maybePath = ExtractQuotedPath(line) ?? ExtractPathAfterColon(line);
                                    if (!string.IsNullOrEmpty(maybePath))
                                        lastPath = maybePath.Trim().Trim('"');
                                    progress?.Report(new YtDlpDownloadProgress
                                    {
                                        StatusText = Truncate(line.Trim(), 120),
                                    });
                                }
                                else if (line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase)
                                    || line.StartsWith("[info]", StringComparison.OrdinalIgnoreCase)
                                    || line.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase))
                                {
                                    progress?.Report(new YtDlpDownloadProgress
                                    {
                                        StatusText = Truncate(line.Trim(), 120),
                                    });
                                }
                            }
                        }

                        Task stdoutTask = Task.Run(() => ReadJavaStream(process.InputStream), cancellationToken);
                        Task stderrTask = Task.Run(() => ReadJavaStream(process.ErrorStream), cancellationToken);
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

        private static string ExtractQuotedPath(string line)
        {
            int first = line.IndexOf('"');
            int last = line.LastIndexOf('"');
            if (first >= 0 && last > first)
                return line.Substring(first + 1, last - first - 1);
            return null;
        }

        private static string ExtractPathAfterColon(string line)
        {
            int idx = line.IndexOf(':');
            if (idx < 0 || idx + 1 >= line.Length)
                return null;
            string rest = line.Substring(idx + 1).Trim();
            return string.IsNullOrEmpty(rest) ? null : rest;
        }

        private static string TryFindNewestFile(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return null;
                string newest = null;
                DateTime newestTime = DateTime.MinValue;
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    DateTime t = File.GetLastWriteTimeUtc(file);
                    if (t > newestTime)
                    {
                        newestTime = t;
                        newest = file;
                    }
                }

                return newest;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static YtDlpDownloadResult Fail(string message)
        {
            AnoteFileLog.Append("ytdlp", message ?? "unknown error");
            return new YtDlpDownloadResult { Success = false, Message = message ?? "Download failed" };
        }

        private static string FirstUsefulLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    continue;
                return Truncate(line, 200);
            }

            return Truncate(text.Trim(), 200);
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;
            return text.Substring(0, max) + "…";
        }
    }
}
