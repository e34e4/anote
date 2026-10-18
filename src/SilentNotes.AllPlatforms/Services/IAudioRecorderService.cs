// Copyright © 2025 aNote.
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
    /// Provides sound recording, playback, and management of recorded audio files for the Rec tab.
    /// </summary>
    public interface IAudioRecorderService
    {
        /// <summary>Gets a value indicating whether the platform supports audio recording.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether the microphone permission has been granted.</summary>
        bool HasPermission { get; }

        /// <summary>Requests the microphone permission from the user.</summary>
        /// <returns>True if granted, otherwise false.</returns>
        Task<bool> RequestPermissionAsync();

        /// <summary>Gets a value indicating whether a recording is currently in progress.</summary>
        bool IsRecording { get; }

        /// <summary>Gets a value indicating whether the active recording is paused.</summary>
        bool IsRecordingPaused { get; }

        /// <summary>Gets the elapsed recording duration.</summary>
        TimeSpan RecordingElapsed { get; }

        /// <summary>Gets the file name of the recording currently being played back, or null.</summary>
        string ActivePlayingFile { get; }

        /// <summary>Gets a value indicating whether audio playback is currently playing.</summary>
        bool IsPlaying { get; }

        /// <summary>Gets the current playback position.</summary>
        TimeSpan PlaybackPosition { get; }

        /// <summary>Gets the total duration of the currently playing audio.</summary>
        TimeSpan PlaybackDuration { get; }

        /// <summary>Raised when recording or playback state/progress changes.</summary>
        event Action StateChanged;

        /// <summary>Starts a new sound recording.</summary>
        /// <returns>True if recording started, otherwise false.</returns>
        Task<bool> StartRecordingAsync();

        /// <summary>Pauses the ongoing sound recording.</summary>
        void PauseRecording();

        /// <summary>Resumes a paused sound recording.</summary>
        void ResumeRecording();

        /// <summary>Stops and saves the ongoing sound recording.</summary>
        /// <returns>The saved recording model, or null if failed.</returns>
        Task<AudioRecordingModel> StopRecordingAsync();

        /// <summary>Cancels and discards the ongoing sound recording.</summary>
        void CancelRecording();

        /// <summary>Lists all saved recordings, newest first.</summary>
        IReadOnlyList<AudioRecordingModel> GetRecordings();

        /// <summary>Renames an existing recording file.</summary>
        /// <param name="oldFileName">Existing file name.</param>
        /// <param name="newFileName">New file name (extension is preserved if omitted).</param>
        /// <returns>True if renamed, otherwise false.</returns>
        bool RenameRecording(string oldFileName, string newFileName);

        /// <summary>Deletes a recording file.</summary>
        /// <param name="fileName">The file name to delete.</param>
        /// <returns>True if deleted, otherwise false.</returns>
        bool DeleteRecording(string fileName);

        /// <summary>Starts or resumes playback of a recording file.</summary>
        /// <param name="fileName">The file name to play.</param>
        void Play(string fileName);

        /// <summary>Pauses audio playback.</summary>
        void PausePlayback();

        /// <summary>Stops audio playback.</summary>
        void StopPlayback();

        /// <summary>Saves a copy of the recording into the public Music/Recordings folder of the device.</summary>
        /// <param name="fileName">The file name to export.</param>
        /// <returns>Display path where the recording was saved, or null if failed.</returns>
        Task<string> SaveToPublicStorageAsync(string fileName);

        /// <summary>Shares the recording file with other applications.</summary>
        /// <param name="fileName">The file name to share.</param>
        /// <returns>True if share chooser was opened, otherwise false.</returns>
        Task<bool> ShareRecordingAsync(string fileName);
    }
}
