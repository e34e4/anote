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
    /// Abro download settings, history, and multi-thread HTTP downloads.
    /// </summary>
    public interface IAbroDownloadService
    {
        /// <summary>Gets the absolute path of the store file.</summary>
        string StoreFilePath { get; }

        /// <summary>Loads the store from disk.</summary>
        AbroDownloadStoreModel LoadOrDefault();

        /// <summary>Saves the store.</summary>
        bool TrySave(AbroDownloadStoreModel store);

        /// <summary>Resolves the effective download directory (creates it).</summary>
        string GetDownloadDirectory();

        /// <summary>Sets the download directory path (empty = default <c>abro/down</c>).</summary>
        bool SetDownloadDirectory(string absolutePath);

        /// <summary>Gets the clamped thread count (1–5).</summary>
        int ThreadCount { get; }

        /// <summary>Sets the thread count (clamped to 1–5).</summary>
        bool SetThreadCount(int threadCount);

        /// <summary>Removes one history entry (does not delete the file on disk).</summary>
        bool RemoveHistoryItem(Guid id);

        /// <summary>
        /// Starts a download in the background, records history, and returns the history item immediately.
        /// </summary>
        AbroDownloadHistoryItemModel StartDownload(
            string url,
            string fileNameHint,
            string userAgent,
            string cookies,
            long? contentLengthHint = null);

        /// <summary>
        /// Downloads <paramref name="url"/> to the configured folder (awaitable).
        /// </summary>
        Task<AbroDownloadHistoryItemModel> DownloadAsync(
            string url,
            string fileNameHint,
            string userAgent,
            string cookies,
            long? contentLengthHint = null,
            CancellationToken cancellationToken = default);
    }
}
