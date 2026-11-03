// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// Local / synced store for Abro browser bookmarks.
    /// </summary>
    public class AbroBookmarkStoreModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroBookmarkStoreModel"/> class.
        /// </summary>
        public AbroBookmarkStoreModel()
        {
            Version = 1;
            UpdatedAt = DateTime.UtcNow;
            Items = new List<AbroBookmarkItemModel>();
        }

        /// <summary>Gets or sets the store format version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last store-wide change.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>Gets or sets the bookmark items.</summary>
        public List<AbroBookmarkItemModel> Items { get; set; }

        /// <summary>
        /// Touches <see cref="UpdatedAt"/> to now (UTC).
        /// </summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
