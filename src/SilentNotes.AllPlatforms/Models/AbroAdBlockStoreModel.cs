// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// Local store for Abro DNS-style ad blocking.
    /// </summary>
    public class AbroAdBlockStoreModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAdBlockStoreModel"/> class.
        /// </summary>
        public AbroAdBlockStoreModel()
        {
            Version = 1;
            Enabled = false;
            UpdatedAt = DateTime.UtcNow;
            Subscriptions = new List<AbroAdBlockSubscriptionModel>();
        }

        /// <summary>Gets or sets the store format version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets whether request filtering is active in Abro.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last store-wide change.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>Gets or sets when the compiled domain set was last rebuilt (UTC).</summary>
        public DateTime? CompiledAt { get; set; }

        /// <summary>Gets or sets the number of domains in the compiled set.</summary>
        public int CompiledDomainCount { get; set; }

        /// <summary>Gets or sets the configured subscriptions.</summary>
        public List<AbroAdBlockSubscriptionModel> Subscriptions { get; set; }

        /// <summary>
        /// Touches <see cref="UpdatedAt"/> to now (UTC).
        /// </summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
