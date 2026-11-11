// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Locates optional ffmpeg / yt-dlp binaries shipped or placed under the anote storage tree.
    /// </summary>
    public interface IMediaToolsService
    {
        /// <summary>Resolved ffmpeg path, or null when not present.</summary>
        string FfmpegPath { get; }

        /// <summary>Resolved yt-dlp path, or null when not present.</summary>
        string YtDlpPath { get; }

        /// <summary>Ensures tool directories exist and refreshes resolved paths.</summary>
        void EnsureReady();
    }
}
