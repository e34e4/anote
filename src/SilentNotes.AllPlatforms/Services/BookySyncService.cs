// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class BookySyncService : IBookySyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly IRepositoryStorageService _repositoryService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;
        private readonly IMessengerService _messengerService;

        /// <summary>
        /// Initializes a new instance of the <see cref="BookySyncService"/> class.
        /// </summary>
        public BookySyncService(
            ISettingsService settingsService,
            IRepositoryStorageService repositoryService,
            ICloudStorageClientFactory cloudStorageClientFactory,
            IMessengerService messengerService)
        {
            _settingsService = settingsService;
            _repositoryService = repositoryService;
            _cloudStorageClientFactory = cloudStorageClientFactory;
            _messengerService = messengerService;
        }

        /// <inheritdoc/>
        public bool IsConfigured
        {
            get
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                return settings.BookySyncEnabled
                    && !string.IsNullOrWhiteSpace(settings.BookySyncUrl)
                    && !string.IsNullOrWhiteSpace(settings.BookySyncRemotePath);
            }
        }

        /// <inheritdoc/>
        public async Task<BookySyncResult> SyncAsync()
        {
            CloudStorageCredentials credentials = null;
            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (!settings.BookySyncEnabled)
                    return BookySyncResult.Disabled;

                if (string.IsNullOrWhiteSpace(settings.BookySyncUrl))
                    return BookySyncResult.NotConfigured;

                (string folderUrl, string fileName) = SplitRemotePath(settings.BookySyncUrl, settings.BookySyncRemotePath);
                credentials = BuildCredentials(folderUrl, settings);
                ICloudStorageClient client = _cloudStorageClientFactory.GetByKey(CloudStorageClientFactory.CloudStorageIdWebdav);

                // Download the shared file, a missing file is treated as an empty collection.
                BookmarkListModel remote = new BookmarkListModel();
                if (await client.ExistsFileAsync(fileName, credentials))
                {
                    byte[] bytes = await client.DownloadFileAsync(fileName, credentials);
                    remote = BookyJsonFormat.Parse(Encoding.UTF8.GetString(bytes));
                    if (remote == null)
                        return BookySyncResult.Failed;
                }

                // Merge the remote bookmarks into the repository and store it locally.
                _repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                bool changed = MergeInto(remote, repository.Bookmarks);
                if (changed)
                    _repositoryService.TrySaveRepository(repository);

                // Upload the merged list back to the shared file.
                string json = BookyJsonFormat.Serialize(repository.Bookmarks);
                await client.UploadFileAsync(fileName, Encoding.UTF8.GetBytes(json), credentials);

                if (changed)
                    _messengerService?.Send(new BookyChangedMessage());
                return BookySyncResult.Success;
            }
            catch (Exception)
            {
                return BookySyncResult.Failed;
            }
            finally
            {
                credentials?.Dispose();
            }
        }

        /// <summary>
        /// Merges the bookmarks of the shared file into the local bookmark list, matching by id and
        /// keeping the more recent modification.
        /// </summary>
        /// <param name="remote">The bookmarks of the shared file.</param>
        /// <param name="local">The bookmark list of the repository, which is modified.</param>
        /// <returns>True if the local list was changed, otherwise false.</returns>
        private static bool MergeInto(BookmarkListModel remote, BookmarkListModel local)
        {
            bool changed = false;
            foreach (BookmarkModel remoteItem in remote)
            {
                BookmarkModel localItem = local.FindById(remoteItem.Id);
                if (localItem == null)
                {
                    local.Add(remoteItem);
                    changed = true;
                }
                else if (remoteItem.ModifiedAt > localItem.ModifiedAt)
                {
                    remoteItem.CloneTo(localItem);
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Splits the configured remote path into the url of its parent folder and the file name,
        /// because the WebDAV client addresses a single directory via the credentials url.
        /// </summary>
        private static (string FolderUrl, string FileName) SplitRemotePath(string baseUrl, string remotePath)
        {
            string basePart = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            string path = (remotePath ?? string.Empty).Trim().TrimStart('/');
            if (path.Length == 0)
                path = "bookmarks.json";

            int lastSlash = path.LastIndexOf('/');
            string folder = (lastSlash >= 0) ? path.Substring(0, lastSlash) : string.Empty;
            string fileName = (lastSlash >= 0) ? path.Substring(lastSlash + 1) : path;

            string folderUrl = (folder.Length > 0) ? basePart + "/" + folder : basePart;
            return (folderUrl, fileName);
        }

        private static CloudStorageCredentials BuildCredentials(string folderUrl, SettingsModel settings)
        {
            var credentials = new CloudStorageCredentials
            {
                CloudStorageId = CloudStorageClientFactory.CloudStorageIdWebdav,
                Url = folderUrl,
                Username = settings.BookySyncUsername,
            };
            credentials.UnprotectedPassword = settings.BookySyncPassword ?? string.Empty;
            return credentials;
        }
    }
}
