// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

namespace SilentNotes.Models
{
    /// <summary>
    /// List of <see cref="BookmarkModel"/> objects, used by a <see cref="NoteRepositoryModel"/>.
    /// </summary>
    public class BookmarkListModel : List<BookmarkModel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BookmarkListModel"/> class.
        /// </summary>
        public BookmarkListModel()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BookmarkListModel"/> class.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new list.</param>
        public BookmarkListModel(IEnumerable<BookmarkModel> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Searches for a bookmark with a given id in the list and returns the found bookmark.
        /// </summary>
        /// <param name="id">Search for the bookmark with this id.</param>
        /// <returns>Found bookmark, or null if no such bookmark exists.</returns>
        public BookmarkModel FindById(Guid id)
        {
            return Find(item => item.Id == id);
        }

        /// <summary>
        /// Determines whether a bookmark with a given id is in the list.
        /// </summary>
        /// <param name="id">Search for the bookmark with this id.</param>
        /// <returns>Returns true if the bookmark is in the list, otherwise false.</returns>
        public bool ContainsById(Guid id)
        {
            return FindById(id) != null;
        }

        /// <summary>
        /// Sorts the bookmarks by <see cref="BookmarkModel.CreatedAt"/>, newest first.
        /// </summary>
        public void SortByCreatedDescending()
        {
            Sort((left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
        }
    }
}
