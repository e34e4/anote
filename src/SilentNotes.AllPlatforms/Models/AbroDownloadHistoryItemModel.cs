// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// One entry in the Abro download history.
    /// </summary>
    public class AbroDownloadHistoryItemModel
    {
        /// <summary>Completed successfully.</summary>
        public const string StatusCompleted = "completed";

        /// <summary>Failed.</summary>
        public const string StatusFailed = "failed";

        /// <summary>In progress.</summary>
        public const string StatusDownloading = "downloading";

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroDownloadHistoryItemModel"/> class.
        /// </summary>
        public AbroDownloadHistoryItemModel()
        {
            Id = Guid.NewGuid();
            Url = string.Empty;
            FileName = string.Empty;
            FilePath = string.Empty;
            Status = StatusDownloading;
            StartedAt = DateTime.UtcNow;
        }

        /// <summary>Gets or sets the unique id.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the source URL.</summary>
        public string Url { get; set; }

        /// <summary>Gets or sets the saved file name.</summary>
        public string FileName { get; set; }

        /// <summary>Gets or sets the absolute path of the saved file.</summary>
        public string FilePath { get; set; }

        /// <summary>Gets or sets when the download started (UTC).</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>Gets or sets when the download finished (UTC), if any.</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Gets or sets the final byte size, if known.</summary>
        public long Bytes { get; set; }

        /// <summary>Gets or sets the status string.</summary>
        public string Status { get; set; }

        /// <summary>Gets or sets an error message when failed.</summary>
        public string Error { get; set; }

        /// <summary>Gets or sets how many threads were used.</summary>
        public int ThreadCount { get; set; }
    }
}
