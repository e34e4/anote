// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// A single Abro browsing-history visit (local / WebDAV sync).
    /// </summary>
    public class AbroHistoryItemModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroHistoryItemModel"/> class.
        /// </summary>
        public AbroHistoryItemModel()
        {
            Id = Guid.NewGuid();
            Url = string.Empty;
            Title = string.Empty;
            VisitedAt = DateTime.UtcNow;
        }

        /// <summary>Gets or sets the stable history entry id used for sync merges.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the visited URL.</summary>
        public string Url { get; set; }

        /// <summary>Gets or sets the page title at visit time.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the visit.</summary>
        public DateTime VisitedAt { get; set; }

        /// <summary>
        /// Copies all fields onto <paramref name="target"/>.
        /// </summary>
        public void CloneTo(AbroHistoryItemModel target)
        {
            if (target == null)
                return;
            target.Id = Id;
            target.Url = Url ?? string.Empty;
            target.Title = Title ?? string.Empty;
            target.VisitedAt = VisitedAt;
        }
    }
}
