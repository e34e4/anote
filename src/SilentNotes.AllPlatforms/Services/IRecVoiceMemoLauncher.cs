// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Launches on-device speech recognition to transcribe a saved audio recording into the Voice note.
    /// </summary>
    public interface IRecVoiceMemoLauncher
    {
        /// <summary>Gets a value indicating whether voice memo transcription is available.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Starts dictation for an existing recording file; returns false when launch failed.
        /// </summary>
        bool TryLaunchTranscribe(string recordingFilePath, string recordingFileName);
    }
}