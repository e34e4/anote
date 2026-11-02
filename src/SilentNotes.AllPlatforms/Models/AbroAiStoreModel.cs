// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

namespace SilentNotes.Models
{
    /// <summary>
    /// Local / synced store for Abro AI providers and conversations.
    /// </summary>
    public class AbroAiStoreModel
    {
        /// <summary>Maximum number of conversations kept after save/sync.</summary>
        public const int MaxConversations = 50;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiStoreModel"/> class.
        /// </summary>
        public AbroAiStoreModel()
        {
            Version = 1;
            UpdatedAt = DateTime.UtcNow;
            Providers = new List<AbroAiProviderModel>();
            Conversations = new List<AbroAiConversationModel>();
        }

        /// <summary>Gets or sets the store format version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last store-wide change.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>Gets or sets the id of the default provider, or null to use the first.</summary>
        public Guid? DefaultProviderId { get; set; }

        /// <summary>Gets or sets the configured API providers.</summary>
        public List<AbroAiProviderModel> Providers { get; set; }

        /// <summary>Gets or sets the saved conversations.</summary>
        public List<AbroAiConversationModel> Conversations { get; set; }

        /// <summary>
        /// Returns the default provider, falling back to the first entry, or null if empty.
        /// </summary>
        public AbroAiProviderModel GetDefaultProvider()
        {
            if (Providers == null || Providers.Count == 0)
                return null;

            if (DefaultProviderId.HasValue)
            {
                AbroAiProviderModel match = Providers.Find(p => p.Id == DefaultProviderId.Value);
                if (match != null)
                    return match;
            }

            return Providers[0];
        }

        /// <summary>
        /// Drops older conversations beyond <see cref="MaxConversations"/>.
        /// </summary>
        public void TrimConversations()
        {
            if (Conversations == null || Conversations.Count <= MaxConversations)
                return;

            Conversations = Conversations
                .OrderByDescending(c => c.ModifiedAt)
                .Take(MaxConversations)
                .ToList();
        }

        /// <summary>
        /// Touches <see cref="UpdatedAt"/> to now (UTC).
        /// </summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
