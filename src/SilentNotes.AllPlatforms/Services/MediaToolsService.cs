// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class MediaToolsService : IMediaToolsService
    {
        /// <inheritdoc/>
        public string FfmpegPath { get; private set; }

        /// <inheritdoc/>
        public string YtDlpPath { get; private set; }

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
            FfmpegPath = AnotePaths.ResolveFfmpegBinary();
            YtDlpPath = AnotePaths.ResolveYtDlpBinary();
        }
    }
}
