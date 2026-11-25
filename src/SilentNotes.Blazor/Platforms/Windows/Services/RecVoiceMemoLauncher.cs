// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Stub <see cref="IRecVoiceMemoLauncher"/> for Windows.
    /// </summary>
    internal class RecVoiceMemoLauncher : IRecVoiceMemoLauncher
    {
        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public bool TryLaunchTranscribe(string recordingFilePath, string recordingFileName) => false;
    }
}