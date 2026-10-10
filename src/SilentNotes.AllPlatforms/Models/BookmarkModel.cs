// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of a single Booky bookmark. The bookmark points to a page URL and
    /// optionally stores a selected text quote, so the browser can scroll back to it.
    /// </summary>
    public class BookmarkModel
    {
        private Guid _id;

        /// <summary>
        /// Initializes a new instance of the <see cref="BookmarkModel"/> class.
        /// </summary>
        public BookmarkModel()
        {
            CreatedAt = DateTime.UtcNow;
            ModifiedAt = CreatedAt;
        }

        /// <summary>
        /// Gets or sets the unique id of the bookmark.
        /// </summary>
        [XmlAttribute("id")]
        public Guid Id
        {
            get { return (_id != Guid.Empty) ? _id : (_id = Guid.NewGuid()); }
            set { _id = value; }
        }

        /// <summary>
        /// Gets or sets the time in UTC, when the bookmark was created.
        /// </summary>
        [XmlAttribute("created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the time in UTC, when the bookmark was last modified.
        /// </summary>
        [XmlAttribute("modified_at")]
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Gets or sets the title of the bookmarked page.
        /// </summary>
        [XmlElement("title")]
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the url of the bookmarked page.
        /// </summary>
        [XmlElement("url")]
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets the selected text which should be scrolled into view.
        /// </summary>
        [XmlElement("quote")]
        public string Quote { get; set; }

        /// <summary>
        /// Gets or sets an optional user note.
        /// </summary>
        [XmlElement("note")]
        public string Note { get; set; }

        /// <summary>
        /// Sets the <see cref="ModifiedAt"/> property to the current UTC time.
        /// </summary>
        public void RefreshModifiedAt()
        {
            ModifiedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Makes <paramref name="target"/> a deep copy of the bookmark.
        /// </summary>
        /// <param name="target">Copy all properties to this bookmark.</param>
        public void CloneTo(BookmarkModel target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (Object.ReferenceEquals(this, target))
                return;

            target.Id = this.Id;
            target.CreatedAt = this.CreatedAt;
            target.ModifiedAt = this.ModifiedAt;
            target.Title = this.Title;
            target.Url = this.Url;
            target.Quote = this.Quote;
            target.Note = this.Note;
        }

        /// <summary>
        /// Makes a deep copy of the bookmark.
        /// </summary>
        /// <returns>Copy of the bookmark.</returns>
        public BookmarkModel Clone()
        {
            BookmarkModel result = new BookmarkModel();
            CloneTo(result);
            return result;
        }
    }
}
