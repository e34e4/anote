// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms.Services
{
    /// <summary>Windows stub — yt-dlp download is Android-only for now.</summary>
    internal sealed class YtDlpDownloadService : IYtDlpDownloadService
    {
        public bool IsAvailable => false;

        public Task<YtDlpDownloadResult> DownloadAsync(
            string url,
            YtDlpQualityChoice choice,
            IProgress<YtDlpDownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new YtDlpDownloadResult
            {
                Success = false,
                Message = "yt-dlp download is not supported on Windows.",
            });
        }
    }

    /// <summary>Windows stub — always plays the URL.</summary>
    internal sealed class YtDlpDownloadPromptService : IYtDlpDownloadPromptService
    {
        public async Task HandlePlayOrDownloadAsync(string url, Func<Task> playAsync)
        {
            if (playAsync != null)
                await playAsync().ConfigureAwait(true);
        }

        public Task PromptQualityAndDownloadAsync(string url)
        {
            return Task.CompletedTask;
        }
    }
}
