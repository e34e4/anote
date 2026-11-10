// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// A single Abro browser bookmark (local / WebDAV sync).
    /// </summary>
    public class AbroBookmarkItemModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroBookmarkItemModel"/> class.
        /// </summary>
        public AbroBookmarkItemModel()
        {
            Id = Guid.NewGuid();
            Url = string.Empty;
            Title = string.Empty;
            Group = string.Empty;
            ModifiedAt = DateTime.UtcNow;
        }

        /// <summary>Gets or sets the stable bookmark id used for sync merges.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the bookmark URL.</summary>
        public string Url { get; set; }

        /// <summary>Gets or sets the bookmark title.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the optional group name (linqs-style).</summary>
        public string Group { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last local/remote change.</summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Copies all fields onto <paramref name="target"/>.
        /// </summary>
        public void CloneTo(AbroBookmarkItemModel target)
        {
            if (target == null)
                return;
            target.Id = Id;
            target.Url = Url ?? string.Empty;
            target.Title = Title ?? string.Empty;
            target.Group = Group ?? string.Empty;
            target.ModifiedAt = ModifiedAt;
        }
    }
}
