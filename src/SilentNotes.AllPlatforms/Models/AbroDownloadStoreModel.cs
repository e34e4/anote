// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

namespace SilentNotes.Models
{
    /// <summary>
    /// Abro download settings and history.
    /// </summary>
    public class AbroDownloadStoreModel
    {
        /// <summary>Maximum history entries kept.</summary>
        public const int MaxHistory = 200;

        /// <summary>Default relative folder under the Abro root.</summary>
        public const string DefaultFolderName = "down";

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroDownloadStoreModel"/> class.
        /// </summary>
        public AbroDownloadStoreModel()
        {
            Version = 1;
            UpdatedAt = DateTime.UtcNow;
            ThreadCount = 1;
            DownloadDirectory = string.Empty;
            History = new List<AbroDownloadHistoryItemModel>();
        }

        /// <summary>Gets or sets the store format version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last store change.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Gets or sets the absolute download directory.
        /// Empty means the default <c>{abro}/down</c>.
        /// </summary>
        public string DownloadDirectory { get; set; }

        /// <summary>Gets or sets parallel download threads (1–5).</summary>
        public int ThreadCount { get; set; }

        /// <summary>Gets or sets recent downloads (newest first).</summary>
        public List<AbroDownloadHistoryItemModel> History { get; set; }

        /// <summary>Clamps <see cref="ThreadCount"/> into 1–5.</summary>
        public int GetClampedThreadCount()
        {
            if (ThreadCount < 1)
                return 1;
            if (ThreadCount > 5)
                return 5;
            return ThreadCount;
        }

        /// <summary>Drops older history beyond <see cref="MaxHistory"/>.</summary>
        public void TrimHistory()
        {
            if (History == null || History.Count <= MaxHistory)
                return;

            History = History
                .OrderByDescending(h => h.StartedAt)
                .Take(MaxHistory)
                .ToList();
        }

        /// <summary>Touches <see cref="UpdatedAt"/>.</summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
