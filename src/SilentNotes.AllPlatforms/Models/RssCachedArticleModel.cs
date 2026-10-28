// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// A locally cached RSS/Atom article. Stored in a device-local SQLite database and not
    /// synchronized with the note repository.
    /// </summary>
    public class RssCachedArticleModel
    {
        /// <summary>
        /// Gets or sets the id of the feed subscription this article belongs to.
        /// </summary>
        public Guid FeedId { get; set; }

        /// <summary>
        /// Gets or sets the stable article key (guid / link / title+date) used for deduplication.
        /// </summary>
        public string ItemKey { get; set; }

        /// <summary>
        /// Gets or sets the article title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the article link URL.
        /// </summary>
        public string Link { get; set; }

        /// <summary>
        /// Gets or sets a short plain-text summary.
        /// </summary>
        public string Summary { get; set; }

        /// <summary>
        /// Gets or sets the sanitized HTML body of the article.
        /// </summary>
        public string ContentHtml { get; set; }

        /// <summary>
        /// Gets or sets the publication time in UTC, when known.
        /// </summary>
        public DateTime? PublishedAt { get; set; }

        /// <summary>
        /// Gets or sets when the article was last written to the local cache (UTC).
        /// </summary>
        public DateTime CachedAt { get; set; }

        /// <summary>
        /// Gets or sets the approximate UTF-8 byte size of the stored fields, used for size capping.
        /// </summary>
        public long ByteSize { get; set; }
    }
}
