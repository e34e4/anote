// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of a subscribed RSS/Atom feed. Feeds are part of the note repository
    /// and are synchronized together with notes.
    /// </summary>
    public class RssFeedModel
    {
        private Guid _id;

        /// <summary>
        /// Initializes a new instance of the <see cref="RssFeedModel"/> class.
        /// </summary>
        public RssFeedModel()
        {
            CreatedAt = DateTime.UtcNow;
            ModifiedAt = CreatedAt;
        }

        /// <summary>
        /// Gets or sets the unique id of the feed subscription.
        /// </summary>
        [XmlAttribute("id")]
        public Guid Id
        {
            get { return (_id != Guid.Empty) ? _id : (_id = Guid.NewGuid()); }
            set { _id = value; }
        }

        /// <summary>
        /// Gets or sets the time in UTC, when the feed was created.
        /// </summary>
        [XmlAttribute("created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the time in UTC, when the feed was last modified.
        /// </summary>
        [XmlAttribute("modified_at")]
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Gets or sets the display title of the feed.
        /// </summary>
        [XmlElement("title")]
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the URL of the RSS/Atom feed XML.
        /// </summary>
        [XmlElement("url")]
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets an optional site / HTML URL associated with the feed.
        /// </summary>
        [XmlElement("site_url")]
        public string SiteUrl { get; set; }

        /// <summary>
        /// Sets the <see cref="ModifiedAt"/> property to the current UTC time.
        /// </summary>
        public void RefreshModifiedAt()
        {
            ModifiedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Makes <paramref name="target"/> a deep copy of the feed.
        /// </summary>
        public void CloneTo(RssFeedModel target)
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
            target.SiteUrl = this.SiteUrl;
        }

        /// <summary>
        /// Makes a deep copy of the feed.
        /// </summary>
        public RssFeedModel Clone()
        {
            RssFeedModel result = new RssFeedModel();
            CloneTo(result);
            return result;
        }
    }
}
