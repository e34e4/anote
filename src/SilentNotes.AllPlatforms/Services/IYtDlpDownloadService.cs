// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>Progress reported while yt-dlp downloads a URL.</summary>
    public sealed class YtDlpDownloadProgress
    {
        public double? Percent { get; set; }

        public string StatusText { get; set; }
    }

    /// <summary>Result of a yt-dlp download attempt.</summary>
    public sealed class YtDlpDownloadResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public string OutputPath { get; set; }
    }

    /// <summary>
    /// Runs the bundled yt-dlp binary to download media into <see cref="AnotePaths.Down"/>.
    /// </summary>
    public interface IYtDlpDownloadService
    {
        /// <summary>True when a yt-dlp binary is available for execution.</summary>
        bool IsAvailable { get; }

        /// <summary>Downloads <paramref name="url"/> using <paramref name="choice"/> into /sdcard/anote/down.</summary>
        Task<YtDlpDownloadResult> DownloadAsync(
            string url,
            YtDlpQualityChoice choice,
            IProgress<YtDlpDownloadProgress> progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// UI flow: play-or-download on Player, and quality+download from URLCheck / other callers.
    /// </summary>
    public interface IYtDlpDownloadPromptService
    {
        /// <summary>
        /// Asks Play vs Download with yt-dlp; Download continues to quality picker and progress UI.
        /// </summary>
        Task HandlePlayOrDownloadAsync(string url, Func<Task> playAsync);

        /// <summary>
        /// Shows the yt-dlp quality dialog and downloads with a progress indicator.
        /// </summary>
        Task PromptQualityAndDownloadAsync(string url);
    }
}
