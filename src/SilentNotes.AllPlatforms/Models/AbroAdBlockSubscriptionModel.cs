// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// A downloaded DNS / hosts ad-blocking subscription.
    /// </summary>
    public class AbroAdBlockSubscriptionModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAdBlockSubscriptionModel"/> class.
        /// </summary>
        public AbroAdBlockSubscriptionModel()
        {
            Id = Guid.NewGuid();
            Url = string.Empty;
            Title = string.Empty;
            Enabled = true;
            CreatedAt = DateTime.UtcNow;
        }

        /// <summary>Gets or sets the unique id.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the subscription list URL.</summary>
        public string Url { get; set; }

        /// <summary>Gets or sets a short display title (often derived from the URL).</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets whether this subscription is included when compiling.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets when the subscription was added (UTC).</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Gets or sets when the list was last downloaded (UTC), if ever.</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Gets or sets how many domains were parsed from the last download.</summary>
        public int DomainCount { get; set; }

        /// <summary>Gets or sets the last download / parse error, if any.</summary>
        public string LastError { get; set; }
    }
}
