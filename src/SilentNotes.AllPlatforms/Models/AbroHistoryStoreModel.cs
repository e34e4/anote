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
    /// Local / synced store for Abro browsing history.
    /// </summary>
    public class AbroHistoryStoreModel
    {
        /// <summary>Maximum number of history entries kept after save/sync.</summary>
        public const int MaxItems = 2000;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroHistoryStoreModel"/> class.
        /// </summary>
        public AbroHistoryStoreModel()
        {
            Version = 1;
            UpdatedAt = DateTime.UtcNow;
            Items = new List<AbroHistoryItemModel>();
        }

        /// <summary>Gets or sets the store format version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last store-wide change.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>Gets or sets the history items.</summary>
        public List<AbroHistoryItemModel> Items { get; set; }

        /// <summary>
        /// Drops older visits beyond <see cref="MaxItems"/>.
        /// </summary>
        public void TrimItems()
        {
            if (Items == null || Items.Count <= MaxItems)
                return;

            Items = Items
                .OrderByDescending(i => i.VisitedAt)
                .Take(MaxItems)
                .ToList();
        }

        /// <summary>
        /// Touches <see cref="UpdatedAt"/> to now (UTC).
        /// </summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
