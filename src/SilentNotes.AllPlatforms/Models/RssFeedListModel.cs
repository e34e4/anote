// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// List of <see cref="RssFeedModel"/> objects, used by a <see cref="NoteRepositoryModel"/>.
    /// </summary>
    public class RssFeedListModel : List<RssFeedModel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RssFeedListModel"/> class.
        /// </summary>
        public RssFeedListModel()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RssFeedListModel"/> class.
        /// </summary>
        public RssFeedListModel(IEnumerable<RssFeedModel> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Searches for a feed with a given id in the list and returns the found feed.
        /// </summary>
        public RssFeedModel FindById(Guid id)
        {
            return Find(item => item.Id == id);
        }

        /// <summary>
        /// Searches for a feed with a given URL (case-insensitive).
        /// </summary>
        public RssFeedModel FindByUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            string needle = url.Trim();
            return Find(item => string.Equals(item.Url, needle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether a feed with a given id is in the list.
        /// </summary>
        public bool ContainsById(Guid id)
        {
            return FindById(id) != null;
        }
    }
}
