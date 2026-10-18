// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Stub implementation of <see cref="IAudioRecorderService"/> for Windows.
    /// </summary>
    internal class AudioRecorderService : IAudioRecorderService
    {
        /// <inheritdoc/>
        public event Action StateChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public bool HasPermission => false;

        /// <inheritdoc/>
        public Task<bool> RequestPermissionAsync() => Task.FromResult(false);

        /// <inheritdoc/>
        public bool IsRecording => false;

        /// <inheritdoc/>
        public bool IsRecordingPaused => false;

        /// <inheritdoc/>
        public TimeSpan RecordingElapsed => TimeSpan.Zero;

        /// <inheritdoc/>
        public string ActivePlayingFile => null;

        /// <inheritdoc/>
        public bool IsPlaying => false;

        /// <inheritdoc/>
        public TimeSpan PlaybackPosition => TimeSpan.Zero;

        /// <inheritdoc/>
        public TimeSpan PlaybackDuration => TimeSpan.Zero;

        /// <inheritdoc/>
        public Task<bool> StartRecordingAsync() => Task.FromResult(false);

        /// <inheritdoc/>
        public void PauseRecording()
        {
        }

        /// <inheritdoc/>
        public void ResumeRecording()
        {
        }

        /// <inheritdoc/>
        public Task<AudioRecordingModel> StopRecordingAsync() => Task.FromResult<AudioRecordingModel>(null);

        /// <inheritdoc/>
        public void CancelRecording()
        {
        }

        /// <inheritdoc/>
        public IReadOnlyList<AudioRecordingModel> GetRecordings() => new List<AudioRecordingModel>();

        /// <inheritdoc/>
        public bool RenameRecording(string oldFileName, string newFileName) => false;

        /// <inheritdoc/>
        public bool DeleteRecording(string fileName) => false;

        /// <inheritdoc/>
        public void Play(string fileName)
        {
        }

        /// <inheritdoc/>
        public void PausePlayback()
        {
        }

        /// <inheritdoc/>
        public void StopPlayback()
        {
        }

        /// <inheritdoc/>
        public Task<string> SaveToPublicStorageAsync(string fileName) => Task.FromResult<string>(null);

        /// <inheritdoc/>
        public Task<bool> ShareRecordingAsync(string fileName) => Task.FromResult(false);
    }
}
