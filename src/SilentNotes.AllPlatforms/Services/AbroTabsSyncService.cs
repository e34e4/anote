// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroTabsSyncService : IAbroTabsSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroTabsSyncService"/> class.
        /// </summary>
        public AbroTabsSyncService(
            ISettingsService settingsService,
            ICloudStorageClientFactory cloudStorageClientFactory)
        {
            _settingsService = settingsService;
            _cloudStorageClientFactory = cloudStorageClientFactory;
        }

        /// <inheritdoc/>
        public string LastError { get; private set; }

        /// <inheritdoc/>
        public bool IsConfigured
        {
            get
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                return settings.AbroTabsSyncEnabled
                    && !string.IsNullOrWhiteSpace(settings.BookySyncUrl)
                    && !string.IsNullOrWhiteSpace(settings.AbroTabsSyncRemotePath);
            }
        }

        /// <summary>Gets the path of the local Abro tabs file.</summary>
        public static string GetLocalTabsPath()
        {
            return Path.Combine(AbroAiStoreService.ResolveRootDirectory(), "tabs.json");
        }

        /// <inheritdoc/>
        public Task<AbroBrowserSyncResult> SyncAsync() => SyncInternalAsync(overwriteRemote: false);

        /// <inheritdoc/>
        public Task<AbroBrowserSyncResult> OverwriteRemoteAsync() => SyncInternalAsync(overwriteRemote: true);

        private async Task<AbroBrowserSyncResult> SyncInternalAsync(bool overwriteRemote)
        {
            CloudStorageCredentials credentials = null;
            LastError = null;
            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (!settings.AbroTabsSyncEnabled)
                    return AbroBrowserSyncResult.Disabled;
                if (string.IsNullOrWhiteSpace(settings.BookySyncUrl))
                    return AbroBrowserSyncResult.NotConfigured;

                (string folderUrl, string fileName) = SplitRemotePath(settings.BookySyncUrl, settings.AbroTabsSyncRemotePath);
                credentials = BuildCredentials(folderUrl, settings);
                ICloudStorageClient client = _cloudStorageClientFactory.GetByKey(CloudStorageClientFactory.CloudStorageIdWebdav);

                AbroTabsStateModel local = ReadLocal();
                AbroTabsStateModel merged = local;

                if (!overwriteRemote && await client.ExistsFileAsync(fileName, credentials).ConfigureAwait(false))
                {
                    byte[] bytes = await client.DownloadFileAsync(fileName, credentials).ConfigureAwait(false);
                    AbroTabsStateModel remote = AbroTabsFormat.Parse(Encoding.UTF8.GetString(bytes));
                    if (remote == null)
                    {
                        LastError = "The tabs file on the server has an unexpected format.";
                        return AbroBrowserSyncResult.Failed;
                    }
                    merged = AbroTabsFormat.Merge(local, remote);
                }

                WriteLocal(merged);
                string json = AbroTabsFormat.Serialize(merged);
                await client.UploadFileAsync(fileName, Encoding.UTF8.GetBytes(json), credentials).ConfigureAwait(false);
                return AbroBrowserSyncResult.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return AbroBrowserSyncResult.Failed;
            }
            finally
            {
                credentials?.Dispose();
            }
        }

        private static AbroTabsStateModel ReadLocal()
        {
            try
            {
                string path = GetLocalTabsPath();
                if (!File.Exists(path))
                    return new AbroTabsStateModel();
                return AbroTabsFormat.Parse(File.ReadAllText(path, Encoding.UTF8)) ?? new AbroTabsStateModel();
            }
            catch (Exception)
            {
                return new AbroTabsStateModel();
            }
        }

        private static void WriteLocal(AbroTabsStateModel state)
        {
            try
            {
                string path = GetLocalTabsPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(path, AbroTabsFormat.Serialize(state), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private static (string FolderUrl, string FileName) SplitRemotePath(string baseUrl, string remotePath)
        {
            string basePart = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            string path = (remotePath ?? string.Empty).Trim().TrimStart('/');
            if (path.Length == 0)
                path = "abro/tabs.json";

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
