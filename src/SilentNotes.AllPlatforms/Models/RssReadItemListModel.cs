// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// List of <see cref="RssReadItemModel"/> objects, used by a <see cref="NoteRepositoryModel"/>.
    /// </summary>
    public class RssReadItemListModel : List<RssReadItemModel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RssReadItemListModel"/> class.
        /// </summary>
        public RssReadItemListModel()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RssReadItemListModel"/> class.
        /// </summary>
        public RssReadItemListModel(IEnumerable<RssReadItemModel> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Finds a read marker by article key.
        /// </summary>
        public RssReadItemModel FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            return Find(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether an article key is marked as read.
        /// </summary>
        public bool ContainsKey(string key)
        {
            return FindByKey(key) != null;
        }
    }
}
