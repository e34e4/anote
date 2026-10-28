// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Abstract SQLite-backed implementation of <see cref="IRssArticleCacheService"/>.
    /// Platform subclasses only provide the storage directory via <see cref="GetLocation"/>.
    /// </summary>
    public abstract class RssArticleCacheServiceBase : IRssArticleCacheService
    {
        public const string DatabaseFileName = "rss_articles.sqlite";

        private static readonly object SyncRoot = new object();
        private bool _schemaReady;

        /// <summary>
        /// Sub classes override this to define the directory where the SQLite file is stored.
        /// </summary>
        protected abstract string GetLocation();

        /// <inheritdoc/>
        public IReadOnlyList<RssCachedArticleModel> LoadArticles(IEnumerable<Guid> feedIds)
        {
            if (feedIds == null)
                return Array.Empty<RssCachedArticleModel>();

            List<Guid> ids = feedIds.Distinct().ToList();
            if (ids.Count == 0)
                return Array.Empty<RssCachedArticleModel>();

            lock (SyncRoot)
            {
                EnsureDatabase();
                var result = new List<RssCachedArticleModel>();
                using (SqliteConnection connection = OpenConnection())
                {
                    string placeholders = string.Join(",", ids.Select((_, i) => "@id" + i.ToString(CultureInfo.InvariantCulture)));
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "SELECT feed_id, item_key, title, link, summary, content_html, published_at, cached_at, byte_size " +
                            "FROM rss_articles WHERE feed_id IN (" + placeholders + ") " +
                            "ORDER BY COALESCE(published_at, cached_at) DESC, cached_at DESC;";
                        for (int i = 0; i < ids.Count; i++)
                            command.Parameters.AddWithValue("@id" + i.ToString(CultureInfo.InvariantCulture), ids[i].ToString("N"));

                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                                result.Add(ReadArticle(reader));
                        }
                    }
                }
                return result;
            }
        }

        /// <inheritdoc/>
        public int MergeArticles(Guid feedId, IReadOnlyList<RssCachedArticleModel> fetched, long maxCacheBytes = IRssArticleCacheService.DefaultMaxCacheBytes)
        {
            if (fetched == null || fetched.Count == 0)
                return 0;

            lock (SyncRoot)
            {
                EnsureDatabase();
                int changed = 0;
                using (SqliteConnection connection = OpenConnection())
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    foreach (RssCachedArticleModel article in fetched)
                    {
                        if (article == null || string.IsNullOrWhiteSpace(article.ItemKey))
                            continue;

                        article.FeedId = feedId;
                        if (article.CachedAt == default)
                            article.CachedAt = DateTime.UtcNow;
                        article.ByteSize = EstimateByteSize(article);

                        if (TryUpsert(connection, article))
                            changed++;
                    }

                    transaction.Commit();
                }

                EnforceSizeLimitUnlocked(maxCacheBytes);
                return changed;
            }
        }

        /// <inheritdoc/>
        public void RemoveFeed(Guid feedId)
        {
            lock (SyncRoot)
            {
                EnsureDatabase();
                using (SqliteConnection connection = OpenConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM rss_articles WHERE feed_id = @feed_id;";
                    command.Parameters.AddWithValue("@feed_id", feedId.ToString("N"));
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <inheritdoc/>
        public void EnforceSizeLimit(long maxCacheBytes = IRssArticleCacheService.DefaultMaxCacheBytes)
        {
            lock (SyncRoot)
            {
                EnsureDatabase();
                EnforceSizeLimitUnlocked(maxCacheBytes);
            }
        }

        private void EnforceSizeLimitUnlocked(long maxCacheBytes)
        {
            if (maxCacheBytes <= 0)
                return;

            using (SqliteConnection connection = OpenConnection())
            {
                long total = GetTotalByteSize(connection);
                if (total <= maxCacheBytes)
                    return;

                // Delete oldest until under the limit (prefer published_at, fall back to cached_at).
                var toDelete = new List<long>();
                using (var select = connection.CreateCommand())
                {
                    select.CommandText =
                        "SELECT rowid, byte_size FROM rss_articles " +
                        "ORDER BY COALESCE(published_at, cached_at) ASC, cached_at ASC, rowid ASC;";
                    using (SqliteDataReader reader = select.ExecuteReader())
                    {
                        while (reader.Read() && total > maxCacheBytes)
                        {
                            long rowId = reader.GetInt64(0);
                            long size = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                            toDelete.Add(rowId);
                            total -= Math.Max(0, size);
                        }
                    }
                }

                if (toDelete.Count == 0)
                    return;

                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    foreach (long rowId in toDelete)
                    {
                        using (var delete = connection.CreateCommand())
                        {
                            delete.Transaction = transaction;
                            delete.CommandText = "DELETE FROM rss_articles WHERE rowid = @rowid;";
                            delete.Parameters.AddWithValue("@rowid", rowId);
                            delete.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }

                // Reclaim disk pages after large deletes; ignore failures.
                try
                {
                    using (var vacuum = connection.CreateCommand())
                    {
                        vacuum.CommandText = "VACUUM;";
                        vacuum.ExecuteNonQuery();
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private bool TryUpsert(SqliteConnection connection, RssCachedArticleModel article)
        {
            RssCachedArticleModel existing = FindExisting(connection, article.FeedId, article.ItemKey);
            if (existing == null)
            {
                Insert(connection, article);
                return true;
            }

            // Same key: keep existing unless fetched is strictly newer by published_at,
            // or published_at is missing and content differs (refresh body).
            bool isNewer = false;
            if (article.PublishedAt != null && existing.PublishedAt != null)
                isNewer = article.PublishedAt.Value > existing.PublishedAt.Value;
            else if (article.PublishedAt != null && existing.PublishedAt == null)
                isNewer = true;
            else if (article.PublishedAt == null && existing.PublishedAt == null)
            {
                isNewer = !string.Equals(existing.ContentHtml ?? string.Empty, article.ContentHtml ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(existing.Title ?? string.Empty, article.Title ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(existing.Summary ?? string.Empty, article.Summary ?? string.Empty, StringComparison.Ordinal);
            }

            if (!isNewer)
                return false;

            Update(connection, article);
            return true;
        }

        private static RssCachedArticleModel FindExisting(SqliteConnection connection, Guid feedId, string itemKey)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT feed_id, item_key, title, link, summary, content_html, published_at, cached_at, byte_size " +
                    "FROM rss_articles WHERE feed_id = @feed_id AND item_key = @item_key LIMIT 1;";
                command.Parameters.AddWithValue("@feed_id", feedId.ToString("N"));
                command.Parameters.AddWithValue("@item_key", itemKey);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                        return ReadArticle(reader);
                }
            }
            return null;
        }

        private static void Insert(SqliteConnection connection, RssCachedArticleModel article)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO rss_articles (feed_id, item_key, title, link, summary, content_html, published_at, cached_at, byte_size) " +
                    "VALUES (@feed_id, @item_key, @title, @link, @summary, @content_html, @published_at, @cached_at, @byte_size);";
                BindArticle(command, article);
                command.ExecuteNonQuery();
            }
        }

        private static void Update(SqliteConnection connection, RssCachedArticleModel article)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "UPDATE rss_articles SET title = @title, link = @link, summary = @summary, content_html = @content_html, " +
                    "published_at = @published_at, cached_at = @cached_at, byte_size = @byte_size " +
                    "WHERE feed_id = @feed_id AND item_key = @item_key;";
                BindArticle(command, article);
                command.ExecuteNonQuery();
            }
        }

        private static void BindArticle(SqliteCommand command, RssCachedArticleModel article)
        {
            command.Parameters.AddWithValue("@feed_id", article.FeedId.ToString("N"));
            command.Parameters.AddWithValue("@item_key", article.ItemKey ?? string.Empty);
            command.Parameters.AddWithValue("@title", (object)article.Title ?? DBNull.Value);
            command.Parameters.AddWithValue("@link", (object)article.Link ?? DBNull.Value);
            command.Parameters.AddWithValue("@summary", (object)article.Summary ?? DBNull.Value);
            command.Parameters.AddWithValue("@content_html", (object)article.ContentHtml ?? DBNull.Value);
            command.Parameters.AddWithValue("@published_at",
                article.PublishedAt == null
                    ? (object)DBNull.Value
                    : article.PublishedAt.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@cached_at",
                article.CachedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@byte_size", article.ByteSize);
        }

        private static RssCachedArticleModel ReadArticle(SqliteDataReader reader)
        {
            return new RssCachedArticleModel
            {
                FeedId = Guid.ParseExact(reader.GetString(0), "N"),
                ItemKey = reader.IsDBNull(1) ? null : reader.GetString(1),
                Title = reader.IsDBNull(2) ? null : reader.GetString(2),
                Link = reader.IsDBNull(3) ? null : reader.GetString(3),
                Summary = reader.IsDBNull(4) ? null : reader.GetString(4),
                ContentHtml = reader.IsDBNull(5) ? null : reader.GetString(5),
                PublishedAt = ParseDate(reader.IsDBNull(6) ? null : reader.GetString(6)),
                CachedAt = ParseDate(reader.IsDBNull(7) ? null : reader.GetString(7)) ?? DateTime.MinValue,
                ByteSize = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
            };
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return parsed.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : parsed.ToUniversalTime();
            return null;
        }

        private static long GetTotalByteSize(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COALESCE(SUM(byte_size), 0) FROM rss_articles;";
                object result = command.ExecuteScalar();
                return (result == null || result == DBNull.Value) ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }
        }

        internal static long EstimateByteSize(RssCachedArticleModel article)
        {
            if (article == null)
                return 0;
            long size = 0;
            size += Encoding.UTF8.GetByteCount(article.FeedId.ToString("N"));
            size += Encoding.UTF8.GetByteCount(article.ItemKey ?? string.Empty);
            size += Encoding.UTF8.GetByteCount(article.Title ?? string.Empty);
            size += Encoding.UTF8.GetByteCount(article.Link ?? string.Empty);
            size += Encoding.UTF8.GetByteCount(article.Summary ?? string.Empty);
            size += Encoding.UTF8.GetByteCount(article.ContentHtml ?? string.Empty);
            return size;
        }

        private void EnsureDatabase()
        {
            if (_schemaReady)
                return;

            Directory.CreateDirectory(GetLocation());
            using (SqliteConnection connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS rss_articles (" +
                    "feed_id TEXT NOT NULL, " +
                    "item_key TEXT NOT NULL, " +
                    "title TEXT, " +
                    "link TEXT, " +
                    "summary TEXT, " +
                    "content_html TEXT, " +
                    "published_at TEXT, " +
                    "cached_at TEXT NOT NULL, " +
                    "byte_size INTEGER NOT NULL DEFAULT 0, " +
                    "PRIMARY KEY (feed_id, item_key)" +
                    ");" +
                    "CREATE INDEX IF NOT EXISTS ix_rss_articles_published ON rss_articles (published_at, cached_at);";
                command.ExecuteNonQuery();
            }
            _schemaReady = true;
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
            connection.Open();
            return connection;
        }

        private string DatabasePath
        {
            get { return Path.Combine(GetLocation(), DatabaseFileName); }
        }
    }
}
