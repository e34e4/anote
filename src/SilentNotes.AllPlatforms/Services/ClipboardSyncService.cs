// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class ClipboardSyncService : IClipboardSyncService
    {
        private readonly IClipboardHistoryService _historyService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardSyncService"/> class.
        /// </summary>
        /// <param name="historyService">The local clipboard history service.</param>
        public ClipboardSyncService(IClipboardHistoryService historyService)
        {
            _historyService = historyService;
        }

        /// <inheritdoc/>
        public void Export(NoteRepositoryModel repository)
        {
            var list = new ClipboardTransferListModel();
            foreach (ClipboardItemModel item in _historyService.LoadHistoryOrDefault().Items)
                list.Add(ToTransferItem(item));
            repository.Clipboard = list;
        }

        /// <inheritdoc/>
        public IReadOnlyList<ClipboardTransferItemModel> MergeAndImport(IReadOnlyList<ClipboardTransferItemModel> cloudItems)
        {
            var merged = new Dictionary<Guid, ClipboardTransferItemModel>();

            // Local items have precedence on a tie.
            foreach (ClipboardItemModel localItem in _historyService.LoadHistoryOrDefault().Items)
                merged[localItem.Id] = ToTransferItem(localItem);

            foreach (ClipboardTransferItemModel cloudItem in cloudItems ?? new List<ClipboardTransferItemModel>())
            {
                if (merged.TryGetValue(cloudItem.Id, out ClipboardTransferItemModel existing))
                {
                    if (cloudItem.ModifiedUtc > existing.ModifiedUtc)
                        merged[cloudItem.Id] = cloudItem.Clone();
                }
                else
                {
                    merged[cloudItem.Id] = cloudItem.Clone();
                }
            }

            // Write the merged result back to the local history, materializing downloaded images.
            ClipboardHistoryModel localModel = _historyService.LoadHistoryOrDefault();
            localModel.Items.Clear();
            foreach (ClipboardTransferItemModel item in merged.Values.OrderByDescending(i => i.CreatedUtc))
                localModel.Items.Add(ToLocalItem(item));
            _historyService.TrySaveHistory(localModel);

            // Return the merged list for uploading (newest first).
            return merged.Values.OrderByDescending(i => i.CreatedUtc).ToList();
        }

        /// <inheritdoc/>
        public void ClearTransfer(NoteRepositoryModel repository)
        {
            repository.Clipboard = new ClipboardTransferListModel();
        }

        /// <inheritdoc/>
        public long GetLocalFingerprint()
        {
            return _historyService.GetModificationFingerprint();
        }

        private ClipboardTransferItemModel ToTransferItem(ClipboardItemModel item)
        {
            var transfer = new ClipboardTransferItemModel
            {
                Id = item.Id,
                CreatedUtc = item.CreatedUtc,
                ModifiedUtc = item.ModifiedUtc,
                IsFavorite = item.IsFavorite,
                IsDeleted = item.IsDeleted,
                Kind = item.Kind,
                ContentHash = item.ContentHash,
                Text = item.Text,
            };

            if ((item.Kind == ClipboardItemKind.Image) && !item.IsDeleted)
            {
                transfer.ImageData = ReadBase64(_historyService.GetFullImagePath(item.ImageFileName));
                transfer.ThumbData = ReadBase64(_historyService.GetThumbImagePath(item.ImageFileName));
            }
            return transfer;
        }

        private ClipboardItemModel ToLocalItem(ClipboardTransferItemModel item)
        {
            var local = new ClipboardItemModel
            {
                Id = item.Id,
                CreatedUtc = item.CreatedUtc,
                ModifiedUtc = item.ModifiedUtc,
                IsFavorite = item.IsFavorite,
                IsDeleted = item.IsDeleted,
                Kind = item.Kind,
                ContentHash = item.ContentHash,
                Text = item.Text,
            };

            if (item.Kind == ClipboardItemKind.Image)
            {
                local.ImageFileName = item.Id.ToString("N") + ".png";
                if (!item.IsDeleted && !string.IsNullOrEmpty(item.ImageData))
                {
                    byte[] full = Convert.FromBase64String(item.ImageData);
                    byte[] thumb = string.IsNullOrEmpty(item.ThumbData) ? null : Convert.FromBase64String(item.ThumbData);
                    _historyService.WriteImageFiles(item.Id, full, thumb);
                }
            }
            return local;
        }

        private static string ReadBase64(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    return Convert.ToBase64String(File.ReadAllBytes(filePath));
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
