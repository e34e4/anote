// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Manages Abro DNS-style ad-blocking subscriptions and the compiled domain filter.
    /// </summary>
    public interface IAbroAdBlockService
    {
        /// <summary>Gets the absolute path of the local store file.</summary>
        string StoreFilePath { get; }

        /// <summary>Loads the store from disk, or returns a new empty store when missing/invalid.</summary>
        AbroAdBlockStoreModel LoadOrDefault();

        /// <summary>Saves the store to disk. Returns true on success.</summary>
        bool TrySave(AbroAdBlockStoreModel store);

        /// <summary>Gets whether filtering is currently enabled.</summary>
        bool IsEnabled { get; }

        /// <summary>Gets the number of domains in the in-memory compiled set.</summary>
        int CompiledDomainCount { get; }

        /// <summary>
        /// Returns true when <paramref name="host"/> (or a parent domain) is blocked.
        /// </summary>
        bool IsBlocked(string host);

        /// <summary>Ensures the compiled domain set is loaded into memory.</summary>
        void EnsureFilterLoaded();

        /// <summary>Sets the enabled flag and persists it.</summary>
        bool SetEnabled(bool enabled);

        /// <summary>Adds a subscription URL (without downloading yet). Returns the new entry, or null on failure.</summary>
        AbroAdBlockSubscriptionModel AddSubscription(string url);

        /// <summary>Removes a subscription and its cached list file.</summary>
        bool RemoveSubscription(Guid subscriptionId);

        /// <summary>
        /// Downloads all enabled subscriptions, recompiles the domain set, and persists metadata.
        /// </summary>
        Task<AbroAdBlockUpdateResult> UpdateSubscriptionsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Result of an ad-block subscription update.</summary>
    public sealed class AbroAdBlockUpdateResult
    {
        /// <summary>Gets or sets how many subscriptions were processed.</summary>
        public int SubscriptionCount { get; set; }

        /// <summary>Gets or sets how many subscriptions downloaded successfully.</summary>
        public int SuccessCount { get; set; }

        /// <summary>Gets or sets how many subscriptions failed.</summary>
        public int FailureCount { get; set; }

        /// <summary>Gets or sets the compiled domain count after update.</summary>
        public int DomainCount { get; set; }

        /// <summary>Gets or sets a short status message.</summary>
        public string Message { get; set; }
    }
}
