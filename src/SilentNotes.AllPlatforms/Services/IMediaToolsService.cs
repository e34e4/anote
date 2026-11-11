// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Locates, installs and updates ffmpeg / yt-dlp binaries under the anote storage tree
    /// and an app-private executable directory.
    /// </summary>
    public interface IMediaToolsService
    {
        /// <summary>Resolved ffmpeg path used for execution, or null when not present.</summary>
        string FfmpegPath { get; }

        /// <summary>Resolved yt-dlp path used for execution, or null when not present.</summary>
        string YtDlpPath { get; }

        /// <summary>Short status for Settings (installed / missing / updating).</summary>
        string FfmpegStatus { get; }

        /// <summary>Short status for Settings (installed / missing / updating).</summary>
        string YtDlpStatus { get; }

        /// <summary>True while an install or update is in progress.</summary>
        bool IsBusy { get; }

        /// <summary>Ensures tool directories exist and refreshes resolved paths.</summary>
        void EnsureReady();

        /// <summary>
        /// Extracts bundled assets when present, otherwise downloads the ABI-matched binaries,
        /// installs them for execution, and mirrors copies under /sdcard/anote.
        /// </summary>
        Task EnsureInstalledAsync(CancellationToken cancellationToken = default);

        /// <summary>Downloads the latest ffmpeg for this device ABI and installs it.</summary>
        Task<bool> UpdateFfmpegAsync(CancellationToken cancellationToken = default);

        /// <summary>Downloads the latest yt-dlp for this device ABI and installs it.</summary>
        Task<bool> UpdateYtDlpAsync(CancellationToken cancellationToken = default);
    }
}
