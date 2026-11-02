// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroAdBlockService : IAbroAdBlockService
    {
        private readonly object _lock = new object();
        private readonly HttpClient _httpClient;
        private readonly string _rootDir;
        private readonly string _listsDir;
        private readonly string _compiledPath;
        private AbroAdBlockStoreModel _cached;
        private HashSet<string> _domains;
        private bool _filterLoaded;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAdBlockService"/> class.
        /// </summary>
        public AbroAdBlockService()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance with an optional shared <see cref="HttpClient"/>.
        /// </summary>
        public AbroAdBlockService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? CreateDefaultHttpClient();
            _rootDir = Path.Combine(AbroAiStoreService.ResolveRootDirectory(), "adblock");
            StoreFilePath = Path.Combine(_rootDir, "store.json");
            _listsDir = Path.Combine(_rootDir, "lists");
            _compiledPath = Path.Combine(_rootDir, "compiled.domains");
            _domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public string StoreFilePath { get; }

        /// <inheritdoc/>
        public bool IsEnabled
        {
            get
            {
                AbroAdBlockStoreModel store = LoadOrDefault();
                return store.Enabled;
            }
        }

        /// <inheritdoc/>
        public int CompiledDomainCount
        {
            get
            {
                EnsureFilterLoaded();
                lock (_lock)
                    return _domains.Count;
            }
        }

        /// <inheritdoc/>
        public AbroAdBlockStoreModel LoadOrDefault()
        {
            lock (_lock)
            {
                if (_cached != null)
                    return _cached;

                try
                {
                    if (File.Exists(StoreFilePath))
                    {
                        string json = File.ReadAllText(StoreFilePath, Encoding.UTF8);
                        AbroAdBlockStoreModel parsed = AbroAdBlockStoreFormat.Parse(json);
                        if (parsed != null)
                        {
                            _cached = parsed;
                            return _cached;
                        }
                    }
                }
                catch (Exception)
                {
                }

                _cached = new AbroAdBlockStoreModel();
                return _cached;
            }
        }

        /// <inheritdoc/>
        public bool TrySave(AbroAdBlockStoreModel store)
        {
            if (store == null)
                return false;

            lock (_lock)
            {
                try
                {
                    store.Touch();
                    Directory.CreateDirectory(_rootDir);
                    string json = AbroAdBlockStoreFormat.Serialize(store);
                    string temp = StoreFilePath + ".tmp";
                    File.WriteAllText(temp, json, Encoding.UTF8);
                    if (File.Exists(StoreFilePath))
                        File.Delete(StoreFilePath);
                    File.Move(temp, StoreFilePath);
                    _cached = store;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsBlocked(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            AbroAdBlockStoreModel store = LoadOrDefault();
            if (!store.Enabled)
                return false;

            EnsureFilterLoaded();
            host = host.Trim().Trim('.').ToLowerInvariant();
            if (host.Length == 0)
                return false;

            lock (_lock)
            {
                if (_domains.Count == 0)
                    return false;

                string current = host;
                while (!string.IsNullOrEmpty(current))
                {
                    if (_domains.Contains(current))
                        return true;
                    int dot = current.IndexOf('.');
                    if (dot < 0 || dot + 1 >= current.Length)
                        break;
                    current = current.Substring(dot + 1);
                    // Avoid matching public suffixes alone like "com".
                    if (current.IndexOf('.') < 0)
                        break;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public void EnsureFilterLoaded()
        {
            lock (_lock)
            {
                if (_filterLoaded)
                    return;

                _domains = LoadCompiledDomainsUnlocked();
                _filterLoaded = true;
            }
        }

        /// <inheritdoc/>
        public bool SetEnabled(bool enabled)
        {
            AbroAdBlockStoreModel store = LoadOrDefault();
            store.Enabled = enabled;
            bool saved = TrySave(store);
            if (saved && enabled)
                EnsureFilterLoaded();
            return saved;
        }

        /// <inheritdoc/>
        public AbroAdBlockSubscriptionModel AddSubscription(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            url = url.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            AbroAdBlockStoreModel store = LoadOrDefault();
            if (store.Subscriptions.Exists(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase)))
                return store.Subscriptions.Find(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase));

            var sub = new AbroAdBlockSubscriptionModel
            {
                Url = url,
                Title = DeriveTitle(uri),
                Enabled = true,
            };
            store.Subscriptions.Add(sub);
            return TrySave(store) ? sub : null;
        }

        /// <inheritdoc/>
        public bool RemoveSubscription(Guid subscriptionId)
        {
            AbroAdBlockStoreModel store = LoadOrDefault();
            int removed = store.Subscriptions.RemoveAll(s => s.Id == subscriptionId);
            if (removed <= 0)
                return false;

            try
            {
                string listPath = GetListPath(subscriptionId);
                if (File.Exists(listPath))
                    File.Delete(listPath);
            }
            catch (Exception)
            {
            }

            if (!TrySave(store))
                return false;

            // Rebuild from remaining lists without network.
            RebuildFromLocalLists(store);
            return true;
        }

        /// <inheritdoc/>
        public async Task<AbroAdBlockUpdateResult> UpdateSubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            AbroAdBlockStoreModel store = LoadOrDefault();
            var result = new AbroAdBlockUpdateResult
            {
                SubscriptionCount = store.Subscriptions.Count,
            };

            if (store.Subscriptions.Count == 0)
            {
                result.Message = "No subscriptions";
                result.DomainCount = 0;
                ClearCompiled(store);
                return result;
            }

            Directory.CreateDirectory(_listsDir);
            var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AbroAdBlockSubscriptionModel sub in store.Subscriptions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!sub.Enabled)
                    continue;

                try
                {
                    using (HttpResponseMessage response = await _httpClient.GetAsync(sub.Url, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        HashSet<string> parsed = AbroAdBlockListParser.Parse(text);
                        string listPath = GetListPath(sub.Id);
                        await File.WriteAllTextAsync(listPath, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                        foreach (string domain in parsed)
                            combined.Add(domain);

                        sub.DomainCount = parsed.Count;
                        sub.UpdatedAt = DateTime.UtcNow;
                        sub.LastError = null;
                        result.SuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    sub.LastError = Truncate(ex.Message, 160);
                    result.FailureCount++;

                    // Keep previously downloaded domains if present.
                    try
                    {
                        string listPath = GetListPath(sub.Id);
                        if (File.Exists(listPath))
                        {
                            string text = File.ReadAllText(listPath, Encoding.UTF8);
                            foreach (string domain in AbroAdBlockListParser.Parse(text))
                                combined.Add(domain);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            WriteCompiled(combined);
            store.CompiledAt = DateTime.UtcNow;
            store.CompiledDomainCount = combined.Count;
            TrySave(store);

            lock (_lock)
            {
                _domains = combined;
                _filterLoaded = true;
            }

            result.DomainCount = combined.Count;
            result.Message = result.FailureCount == 0
                ? $"Updated · {combined.Count} domains"
                : $"Updated with {result.FailureCount} error(s) · {combined.Count} domains";
            return result;
        }

        private void RebuildFromLocalLists(AbroAdBlockStoreModel store)
        {
            var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AbroAdBlockSubscriptionModel sub in store.Subscriptions)
            {
                if (!sub.Enabled)
                    continue;
                try
                {
                    string listPath = GetListPath(sub.Id);
                    if (!File.Exists(listPath))
                        continue;
                    string text = File.ReadAllText(listPath, Encoding.UTF8);
                    HashSet<string> parsed = AbroAdBlockListParser.Parse(text);
                    foreach (string domain in parsed)
                        combined.Add(domain);
                    sub.DomainCount = parsed.Count;
                }
                catch (Exception)
                {
                }
            }

            WriteCompiled(combined);
            store.CompiledAt = DateTime.UtcNow;
            store.CompiledDomainCount = combined.Count;
            TrySave(store);
            lock (_lock)
            {
                _domains = combined;
                _filterLoaded = true;
            }
        }

        private void ClearCompiled(AbroAdBlockStoreModel store)
        {
            try
            {
                if (File.Exists(_compiledPath))
                    File.Delete(_compiledPath);
            }
            catch (Exception)
            {
            }

            store.CompiledAt = DateTime.UtcNow;
            store.CompiledDomainCount = 0;
            TrySave(store);
            lock (_lock)
            {
                _domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _filterLoaded = true;
            }
        }

        private HashSet<string> LoadCompiledDomainsUnlocked()
        {
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(_compiledPath))
                    return domains;

                foreach (string line in File.ReadLines(_compiledPath, Encoding.UTF8))
                {
                    string host = line?.Trim();
                    if (!string.IsNullOrEmpty(host))
                        domains.Add(host);
                }
            }
            catch (Exception)
            {
            }

            return domains;
        }

        private void WriteCompiled(HashSet<string> domains)
        {
            Directory.CreateDirectory(_rootDir);
            string temp = _compiledPath + ".tmp";
            using (var writer = new StreamWriter(temp, false, Encoding.UTF8))
            {
                foreach (string domain in domains)
                    writer.WriteLine(domain);
            }

            if (File.Exists(_compiledPath))
                File.Delete(_compiledPath);
            File.Move(temp, _compiledPath);
        }

        private string GetListPath(Guid subscriptionId)
        {
            return Path.Combine(_listsDir, subscriptionId.ToString("N") + ".txt");
        }

        private static string DeriveTitle(Uri uri)
        {
            string host = uri.Host ?? "list";
            string file = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(file) && file.Length > 1)
                return Truncate(host + " / " + file, 48);
            return Truncate(host, 48);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max - 1) + "…";
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("aNote-Abro/1.0");
            return client;
        }
    }
}
