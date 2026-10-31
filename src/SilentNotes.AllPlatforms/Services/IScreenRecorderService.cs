// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Screen recording for the Rec → Screen sub-tab and the floating quick menu.
    /// Target encode: H.264 SDR 576×1024 (9:16), ~2.2 Mbps, AAC device audio, 2 s keyframes.
    /// </summary>
    public interface IScreenRecorderService
    {
        /// <summary>Gets a value indicating whether screen recording is supported.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether a recording is in progress.</summary>
        bool IsRecording { get; }

        /// <summary>Gets elapsed recording time.</summary>
        TimeSpan RecordingElapsed { get; }

        /// <summary>Raised when recording state or the file list changes.</summary>
        event Action StateChanged;

        /// <summary>Starts screen recording (requests MediaProjection consent on Android).</summary>
        Task<bool> StartRecordingAsync();

        /// <summary>Stops and saves the active recording.</summary>
        Task<AudioRecordingModel> StopRecordingAsync();

        /// <summary>Cancels and discards the active recording.</summary>
        void CancelRecording();

        /// <summary>Lists saved screen recordings, newest first.</summary>
        IReadOnlyList<AudioRecordingModel> GetRecordings();

        /// <summary>Renames a recording; extension is preserved when omitted.</summary>
        bool RenameRecording(string oldFileName, string newFileName);

        /// <summary>Deletes a recording file.</summary>
        bool DeleteRecording(string fileName);

        /// <summary>Exports a copy to public Movies storage.</summary>
        Task<string> SaveToPublicStorageAsync(string fileName);

        /// <summary>Opens the system share sheet for the recording.</summary>
        Task<bool> ShareRecordingAsync(string fileName);
    }
}
