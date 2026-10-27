// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model marking an RSS/Atom article as read. Synced with the repository.
    /// </summary>
    public class RssReadItemModel
    {
        private Guid _id;

        /// <summary>
        /// Initializes a new instance of the <see cref="RssReadItemModel"/> class.
        /// </summary>
        public RssReadItemModel()
        {
            ModifiedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets or sets the unique id of this read marker.
        /// </summary>
        [XmlAttribute("id")]
        public Guid Id
        {
            get { return (_id != Guid.Empty) ? _id : (_id = Guid.NewGuid()); }
            set { _id = value; }
        }

        /// <summary>
        /// Gets or sets the stable key of the article (guid, link, or derived key).
        /// </summary>
        [XmlAttribute("key")]
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the time in UTC, when the article was marked as read.
        /// </summary>
        [XmlAttribute("modified_at")]
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Makes <paramref name="target"/> a deep copy of the read marker.
        /// </summary>
        public void CloneTo(RssReadItemModel target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (Object.ReferenceEquals(this, target))
                return;

            target.Id = this.Id;
            target.Key = this.Key;
            target.ModifiedAt = this.ModifiedAt;
        }

        /// <summary>
        /// Makes a deep copy of the read marker.
        /// </summary>
        public RssReadItemModel Clone()
        {
            RssReadItemModel result = new RssReadItemModel();
            CloneTo(result);
            return result;
        }
    }
}
