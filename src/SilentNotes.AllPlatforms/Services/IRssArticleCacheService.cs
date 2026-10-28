// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Device-local SQLite cache for RSS/Atom article bodies. Feed subscriptions and read-state
    /// remain in the synchronized note repository; this store only keeps article content so the
    /// RSS tab can open instantly and merge newer items on refresh.
    /// </summary>
    public interface IRssArticleCacheService
    {
        /// <summary>Default maximum size of the cache database (~1 GB).</summary>
        public const long DefaultMaxCacheBytes = 1L * 1024 * 1024 * 1024;

        /// <summary>
        /// Loads all cached articles for the given feed ids, newest first.
        /// </summary>
        IReadOnlyList<RssCachedArticleModel> LoadArticles(IEnumerable<Guid> feedIds);

        /// <summary>
        /// Inserts new articles and updates existing ones when the fetched item is newer.
        /// Deduplication uses <see cref="RssCachedArticleModel.ItemKey"/> within a feed.
        /// After merging, enforces the size limit by deleting the oldest articles.
        /// </summary>
        /// <returns>Number of articles inserted or updated.</returns>
        int MergeArticles(Guid feedId, IReadOnlyList<RssCachedArticleModel> fetched, long maxCacheBytes = DefaultMaxCacheBytes);

        /// <summary>
        /// Removes all cached articles for a feed subscription.
        /// </summary>
        void RemoveFeed(Guid feedId);

        /// <summary>
        /// Deletes oldest articles until the approximate stored size is under <paramref name="maxCacheBytes"/>.
        /// </summary>
        void EnforceSizeLimit(long maxCacheBytes = DefaultMaxCacheBytes);
    }
}
