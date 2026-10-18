// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// Represents a sound recording stored by the Rec tab.
    /// </summary>
    public class AudioRecordingModel
    {
        /// <summary>Gets or sets the file name (with extension) of the recording.</summary>
        public string FileName { get; set; }

        /// <summary>Gets or sets the full local file path of the recording.</summary>
        public string FilePath { get; set; }

        /// <summary>Gets or sets the size of the file in bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Gets or sets the duration of the recording in seconds.</summary>
        public double DurationSeconds { get; set; }

        /// <summary>Gets or sets the UTC timestamp when the recording was created.</summary>
        public DateTime CreatedAtUtc { get; set; }
    }
}
