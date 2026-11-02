// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroDownloadService : IAbroDownloadService
    {
        private readonly object _lock = new object();
        private readonly HttpClient _httpClient;
        private readonly string _abroRoot;
        private AbroDownloadStoreModel _cached;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroDownloadService"/> class.
        /// </summary>
        public AbroDownloadService()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance with an optional shared <see cref="HttpClient"/>.
        /// </summary>
        public AbroDownloadService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? CreateDefaultHttpClient();
            _abroRoot = AbroAiStoreService.ResolveRootDirectory();
            StoreFilePath = Path.Combine(_abroRoot, "downloads", "store.json");
        }

        /// <inheritdoc/>
        public string StoreFilePath { get; }

        /// <inheritdoc/>
        public int ThreadCount => LoadOrDefault().GetClampedThreadCount();

        /// <inheritdoc/>
        public AbroDownloadStoreModel LoadOrDefault()
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
                        AbroDownloadStoreModel parsed = AbroDownloadStoreFormat.Parse(json);
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

                _cached = new AbroDownloadStoreModel();
                return _cached;
            }
        }

        /// <inheritdoc/>
        public bool TrySave(AbroDownloadStoreModel store)
        {
            if (store == null)
                return false;

            lock (_lock)
            {
                try
                {
                    store.TrimHistory();
                    store.Touch();
                    string directory = Path.GetDirectoryName(StoreFilePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    string json = AbroDownloadStoreFormat.Serialize(store);
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
        public string GetDownloadDirectory()
        {
            AbroDownloadStoreModel store = LoadOrDefault();
            string path = string.IsNullOrWhiteSpace(store.DownloadDirectory)
                ? Path.Combine(_abroRoot, AbroDownloadStoreModel.DefaultFolderName)
                : store.DownloadDirectory.Trim();
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception)
            {
            }

            return path;
        }

        /// <inheritdoc/>
        public bool SetDownloadDirectory(string absolutePath)
        {
            AbroDownloadStoreModel store = LoadOrDefault();
            string path = absolutePath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                store.DownloadDirectory = string.Empty;
                return TrySave(store);
            }

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception)
            {
                return false;
            }

            store.DownloadDirectory = path;
            return TrySave(store);
        }

        /// <inheritdoc/>
        public bool SetThreadCount(int threadCount)
        {
            AbroDownloadStoreModel store = LoadOrDefault();
            store.ThreadCount = Math.Clamp(threadCount, 1, 5);
            return TrySave(store);
        }

        /// <inheritdoc/>
        public bool RemoveHistoryItem(Guid id)
        {
            AbroDownloadStoreModel store = LoadOrDefault();
            int removed = store.History.RemoveAll(h => h.Id == id);
            return removed > 0 && TrySave(store);
        }

        /// <inheritdoc/>
        public AbroDownloadHistoryItemModel StartDownload(
            string url,
            string fileNameHint,
            string userAgent,
            string cookies,
            long? contentLengthHint = null)
        {
            AbroDownloadHistoryItemModel item = CreatePendingItem(url, fileNameHint);
            PersistHistoryInsert(item);
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunDownloadAsync(item, userAgent, cookies, contentLengthHint, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MarkFailed(item, ex.Message);
                }
            });
            return item;
        }

        /// <inheritdoc/>
        public async Task<AbroDownloadHistoryItemModel> DownloadAsync(
            string url,
            string fileNameHint,
            string userAgent,
            string cookies,
            long? contentLengthHint = null,
            CancellationToken cancellationToken = default)
        {
            AbroDownloadHistoryItemModel item = CreatePendingItem(url, fileNameHint);
            PersistHistoryInsert(item);
            await RunDownloadAsync(item, userAgent, cookies, contentLengthHint, cancellationToken).ConfigureAwait(false);
            return item;
        }

        private AbroDownloadHistoryItemModel CreatePendingItem(string url, string fileNameHint)
        {
            string dir = GetDownloadDirectory();
            string fileName = SanitizeFileName(string.IsNullOrWhiteSpace(fileNameHint) ? "download.bin" : fileNameHint);
            string path = MakeUniquePath(dir, fileName);
            return new AbroDownloadHistoryItemModel
            {
                Url = url ?? string.Empty,
                FileName = Path.GetFileName(path),
                FilePath = path,
                Status = AbroDownloadHistoryItemModel.StatusDownloading,
                ThreadCount = ThreadCount,
            };
        }

        private void PersistHistoryInsert(AbroDownloadHistoryItemModel item)
        {
            AbroDownloadStoreModel store = LoadOrDefault();
            store.History.Insert(0, item);
            TrySave(store);
        }

        private async Task RunDownloadAsync(
            AbroDownloadHistoryItemModel item,
            string userAgent,
            string cookies,
            long? contentLengthHint,
            CancellationToken cancellationToken)
        {
            int threads = Math.Clamp(item.ThreadCount > 0 ? item.ThreadCount : ThreadCount, 1, 5);
            string tempPath = item.FilePath + ".part";

            try
            {
                using (var headRequest = new HttpRequestMessage(HttpMethod.Head, item.Url))
                {
                    ApplyHeaders(headRequest, userAgent, cookies);
                    long? length = contentLengthHint;
                    bool acceptRanges = false;
                    try
                    {
                        using (HttpResponseMessage head = await _httpClient.SendAsync(
                            headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                        {
                            if (head.IsSuccessStatusCode)
                            {
                                if (!length.HasValue || length.Value <= 0)
                                    length = head.Content.Headers.ContentLength;
                                acceptRanges = head.Headers.AcceptRanges.Contains("bytes");
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // HEAD may be unsupported; fall through to GET.
                    }

                    bool useMulti = threads > 1
                        && length.HasValue
                        && length.Value >= 512 * 1024
                        && acceptRanges;

                    if (useMulti)
                    {
                        try
                        {
                            await DownloadMultiAsync(item.Url, tempPath, length.Value, threads, userAgent, cookies, cancellationToken)
                                .ConfigureAwait(false);
                            item.Bytes = length.Value;
                        }
                        catch (Exception)
                        {
                            // Range downloads unsupported — fall back to a single stream.
                            if (File.Exists(tempPath))
                                File.Delete(tempPath);
                            item.Bytes = await DownloadSingleAsync(item.Url, tempPath, userAgent, cookies, cancellationToken)
                                .ConfigureAwait(false);
                            item.ThreadCount = 1;
                        }
                    }
                    else
                    {
                        item.Bytes = await DownloadSingleAsync(item.Url, tempPath, userAgent, cookies, cancellationToken)
                            .ConfigureAwait(false);
                        item.ThreadCount = 1;
                    }
                }

                if (File.Exists(item.FilePath))
                    File.Delete(item.FilePath);
                File.Move(tempPath, item.FilePath);

                item.Status = AbroDownloadHistoryItemModel.StatusCompleted;
                item.CompletedAt = DateTime.UtcNow;
                item.Error = null;
                UpdateHistoryItem(item);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception)
                {
                }

                MarkFailed(item, Truncate(ex.Message, 160));
                throw;
            }
        }

        private async Task DownloadMultiAsync(
            string url,
            string tempPath,
            long totalLength,
            int threads,
            string userAgent,
            string cookies,
            CancellationToken cancellationToken)
        {
            // Pre-allocate file.
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                fs.SetLength(totalLength);
            }

            long chunk = totalLength / threads;
            var tasks = new Task[threads];
            for (int i = 0; i < threads; i++)
            {
                long start = i * chunk;
                long end = (i == threads - 1) ? totalLength - 1 : (start + chunk - 1);
                int index = i;
                tasks[index] = DownloadRangeAsync(url, tempPath, start, end, userAgent, cookies, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task DownloadRangeAsync(
            string url,
            string tempPath,
            long start,
            long end,
            string userAgent,
            string cookies,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                ApplyHeaders(request, userAgent, cookies);
                request.Headers.Range = new RangeHeaderValue(start, end);
                using (HttpResponseMessage response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using (Stream network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (var file = new FileStream(
                        tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, true))
                    {
                        file.Position = start;
                        await network.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<long> DownloadSingleAsync(
            string url,
            string tempPath,
            string userAgent,
            string cookies,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                ApplyHeaders(request, userAgent, cookies);
                using (HttpResponseMessage response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using (Stream network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (var file = new FileStream(
                        tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
                    {
                        await network.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                        return file.Length;
                    }
                }
            }
        }

        private void MarkFailed(AbroDownloadHistoryItemModel item, string error)
        {
            item.Status = AbroDownloadHistoryItemModel.StatusFailed;
            item.CompletedAt = DateTime.UtcNow;
            item.Error = Truncate(error, 160);
            UpdateHistoryItem(item);
        }

        private void UpdateHistoryItem(AbroDownloadHistoryItemModel item)
        {
            AbroDownloadStoreModel store = LoadOrDefault();
            AbroDownloadHistoryItemModel existing = store.History.Find(h => h.Id == item.Id);
            if (existing != null)
            {
                existing.Status = item.Status;
                existing.CompletedAt = item.CompletedAt;
                existing.Bytes = item.Bytes;
                existing.Error = item.Error;
                existing.ThreadCount = item.ThreadCount;
                existing.FileName = item.FileName;
                existing.FilePath = item.FilePath;
            }

            TrySave(store);
        }

        private static void ApplyHeaders(HttpRequestMessage request, string userAgent, string cookies)
        {
            if (!string.IsNullOrWhiteSpace(userAgent))
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (!string.IsNullOrWhiteSpace(cookies))
                request.Headers.TryAddWithoutValidation("Cookie", cookies);
        }

        private static string MakeUniquePath(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
                return path;

            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, $"{name} ({i}){ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{ext}");
        }

        private static string SanitizeFileName(string name)
        {
            name = Path.GetFileName(name?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                name = "download.bin";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            if (name.Length > 120)
            {
                string ext = Path.GetExtension(name);
                name = name.Substring(0, 120 - ext.Length) + ext;
            }

            return name;
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max - 1) + "…";
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromHours(6);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("aNote-Abro/1.0");
            return client;
        }
    }
}
