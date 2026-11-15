// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Downloads a web page, runs it through the readability parser and stores the cleaned article
    /// in the local SQLite cache, so it can be read offline.
    /// </summary>
    public class ArticleOfflineService
    {
        private readonly IRssArticleCacheService _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArticleOfflineService"/> class.
        /// </summary>
        public ArticleOfflineService(IRssArticleCacheService cache)
        {
            _cache = cache;
        }

        /// <summary>
        /// Downloads and caches the readable text of an article.
        /// </summary>
        public async Task<bool> SaveAsync(Guid ownerId, string itemKey, string url, string title)
        {
            if (_cache == null || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(itemKey))
                return false;

            try
            {
                string html = await DownloadHtmlAsync(url);
                if (string.IsNullOrWhiteSpace(html))
                    return false;

                string readable = ReadabilityParser.Extract(html, url);
                if (string.IsNullOrWhiteSpace(readable))
                    return false;

                return _cache.SaveOfflineArticle(ownerId, itemKey, title, url, readable);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Loads the cached offline article, or null when nothing is stored.
        /// </summary>
        public RssCachedArticleModel Load(Guid ownerId, string itemKey)
        {
            try
            {
                return _cache?.LoadArticle(ownerId, itemKey);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Removes the stored offline copy.
        /// </summary>
        public void Remove(Guid ownerId, string itemKey)
        {
            try
            {
                _cache?.RemoveOfflineArticle(ownerId, itemKey);
            }
            catch (Exception)
            {
            }
        }

        private static async Task<string> DownloadHtmlAsync(string url)
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36 aNote/1.0");
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

                using (HttpResponseMessage response = await client.GetAsync(url))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }
    }
}
