// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Abstract base implementation of the <see cref="IClipboardHistoryService"/> interface.
    /// </summary>
    /// <remarks>
    /// The history is stored as a small XML file, writing always reads the current file first.
    /// This way a background service (the accessibility service) and the UI can both modify the
    /// history without keeping the same instance. All accesses are guarded by a process wide lock.
    /// </remarks>
    public abstract class ClipboardHistoryServiceBase : IClipboardHistoryService
    {
        private const string ImageSubFolder = "clipboard_images";
        private static readonly object SyncRoot = new object();

        /// <summary>Gets the injected dependency to an xml file service.</summary>
        protected readonly IXmlFileService _xmlFileService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardHistoryServiceBase"/> class.
        /// </summary>
        /// <param name="xmlFileService">The xml file service used to store the history.</param>
        public ClipboardHistoryServiceBase(IXmlFileService xmlFileService)
        {
            _xmlFileService = xmlFileService;
        }

        /// <summary>
        /// Sub classes should override this method to define the directory, where the history and
        /// the images are stored.
        /// </summary>
        /// <returns>The full directory path for storing the history.</returns>
        protected abstract string GetLocation();

        /// <inheritdoc/>
        public ClipboardHistoryModel LoadHistoryOrDefault()
        {
            try
            {
                if (_xmlFileService.TryLoad(HistoryFilePath, out XDocument xml))
                    return XmlUtils.DeserializeFromXmlDocument<ClipboardHistoryModel>(xml);
            }
            catch (Exception)
            {
            }
            return new ClipboardHistoryModel();
        }

        /// <inheritdoc/>
        public bool TrySaveHistory(ClipboardHistoryModel model)
        {
            try
            {
                return _xmlFileService.TrySerializeAndSave(HistoryFilePath, model);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public ClipboardItemModel AddText(string text, int maxNonFavoriteItems)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            lock (SyncRoot)
            {
                ClipboardHistoryModel model = LoadHistoryOrDefault();
                string hash = ComputeHash(Encoding.UTF8.GetBytes(text));
                ClipboardItemModel item = FindAndPromote(model, hash);
                if (item == null)
                {
                    item = new ClipboardItemModel
                    {
                        Id = Guid.NewGuid(),
                        CreatedUtc = DateTime.UtcNow,
                        Kind = ClipboardItemKind.Text,
                        Text = text,
                        ContentHash = hash,
                    };
                    model.Items.Insert(0, item);
                }
                TrimToLimit(model, maxNonFavoriteItems);
                TrySaveHistory(model);
                return item;
            }
        }

        /// <inheritdoc/>
        public ClipboardItemModel AddImage(byte[] fullImage, byte[] thumbnail, int maxNonFavoriteItems)
        {
            if (fullImage == null || fullImage.Length == 0)
                return null;

            lock (SyncRoot)
            {
                ClipboardHistoryModel model = LoadHistoryOrDefault();
                string hash = ComputeHash(fullImage);
                ClipboardItemModel item = FindAndPromote(model, hash);
                if (item == null)
                {
                    var id = Guid.NewGuid();
                    item = new ClipboardItemModel
                    {
                        Id = id,
                        CreatedUtc = DateTime.UtcNow,
                        Kind = ClipboardItemKind.Image,
                        ContentHash = hash,
                        ImageFileName = id.ToString("N") + ".png",
                    };
                    model.Items.Insert(0, item);
                }
                WriteImageFile(item.ImageFileName, fullImage);
                WriteImageFile(GetThumbFileName(item.ImageFileName), (thumbnail != null && thumbnail.Length > 0) ? thumbnail : fullImage);
                TrimToLimit(model, maxNonFavoriteItems);
                TrySaveHistory(model);
                return item;
            }
        }

        /// <inheritdoc/>
        public void ToggleFavorite(Guid id)
        {
            ModifyItem(id, item => item.IsFavorite = !item.IsFavorite);
        }

        /// <inheritdoc/>
        public void Delete(Guid id)
        {
            lock (SyncRoot)
            {
                ClipboardHistoryModel model = LoadHistoryOrDefault();
                ClipboardItemModel item = model.Items.Find(i => i.Id == id);
                if (item == null)
                    return;

                model.Items.Remove(item);
                DeleteImageFiles(item);
                TrySaveHistory(model);
            }
        }

        /// <inheritdoc/>
        public void ClearNonFavorites()
        {
            lock (SyncRoot)
            {
                ClipboardHistoryModel model = LoadHistoryOrDefault();
                foreach (ClipboardItemModel item in model.Items.Where(i => !i.IsFavorite).ToList())
                    DeleteImageFiles(item);
                model.Items.RemoveAll(i => !i.IsFavorite);
                TrySaveHistory(model);
            }
        }

        /// <inheritdoc/>
        public void ClearAll()
        {
            lock (SyncRoot)
            {
                ClipboardHistoryModel model = LoadHistoryOrDefault();
                foreach (ClipboardItemModel item in model.Items)
                    DeleteImageFiles(item);
                model.Items.Clear();
                TrySaveHistory(model);
            }
        }

        /// <inheritdoc/>
        public string GetFullImagePath(string fileName)
        {
            return string.IsNullOrEmpty(fileName) ? null : Path.Combine(ImageDirectory, fileName);
        }

        /// <inheritdoc/>
        public string GetThumbImagePath(string fileName)
        {
            return string.IsNullOrEmpty(fileName) ? null : Path.Combine(ImageDirectory, GetThumbFileName(fileName));
        }

        private string HistoryFilePath
        {
            get { return Path.Combine(GetLocation(), ClipboardHistoryModel.HistoryFileName); }
        }

        private string ImageDirectory
        {
            get { return Path.Combine(GetLocation(), ImageSubFolder); }
        }

        private void ModifyItem(Guid id, Action<ClipboardItemModel> modify)
        {
            lock (SyncRoot)
            {
                ClipboardHistoryModel model = LoadHistoryOrDefault();
                ClipboardItemModel item = model.Items.Find(i => i.Id == id);
                if (item == null)
                    return;

                modify(item);
                TrySaveHistory(model);
            }
        }

        private static ClipboardItemModel FindAndPromote(ClipboardHistoryModel model, string hash)
        {
            ClipboardItemModel existing = model.Items.Find(i => i.ContentHash == hash);
            if (existing != null)
            {
                existing.CreatedUtc = DateTime.UtcNow;
                model.Items.Remove(existing);
                model.Items.Insert(0, existing);
            }
            return existing;
        }

        private void TrimToLimit(ClipboardHistoryModel model, int maxNonFavoriteItems)
        {
            if (maxNonFavoriteItems < 0)
                return;

            int nonFavorites = model.Items.Count(i => !i.IsFavorite);
            for (int index = model.Items.Count - 1; (index >= 0) && (nonFavorites > maxNonFavoriteItems); index--)
            {
                ClipboardItemModel item = model.Items[index];
                if (item.IsFavorite)
                    continue;

                model.Items.RemoveAt(index);
                DeleteImageFiles(item);
                nonFavorites--;
            }
        }

        private static string GetThumbFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) + "_thumb.png";
        }

        private void WriteImageFile(string fileName, byte[] content)
        {
            try
            {
                Directory.CreateDirectory(ImageDirectory);
                File.WriteAllBytes(Path.Combine(ImageDirectory, fileName), content);
            }
            catch (Exception)
            {
            }
        }

        private void DeleteImageFiles(ClipboardItemModel item)
        {
            if ((item == null) || (item.Kind != ClipboardItemKind.Image) || string.IsNullOrEmpty(item.ImageFileName))
                return;

            TryDeleteFile(GetFullImagePath(item.ImageFileName));
            TryDeleteFile(GetThumbImagePath(item.ImageFileName));
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception)
            {
            }
        }

        private static string ComputeHash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return Convert.ToHexString(sha.ComputeHash(data));
            }
        }
    }
}
