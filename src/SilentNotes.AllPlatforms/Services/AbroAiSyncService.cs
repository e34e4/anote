// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class AbroAiSyncService : IAbroAiSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly IAbroAiStoreService _storeService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiSyncService"/> class.
        /// </summary>
        public AbroAiSyncService(
            ISettingsService settingsService,
            IAbroAiStoreService storeService,
            ICloudStorageClientFactory cloudStorageClientFactory)
        {
            _settingsService = settingsService;
            _storeService = storeService;
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
                return settings.AbroAiSyncEnabled
                    && TryResolveWebdav(settings, out _, out _, out _)
                    && !string.IsNullOrWhiteSpace(
                        string.IsNullOrWhiteSpace(settings.AbroAiSyncRemotePath) ? "ai.json" : settings.AbroAiSyncRemotePath);
            }
        }

        /// <inheritdoc/>
        public Task<AbroAiSyncResult> SyncAsync()
        {
            return SyncInternalAsync(overwriteRemote: false);
        }

        /// <inheritdoc/>
        public Task<AbroAiSyncResult> OverwriteRemoteAsync()
        {
            return SyncInternalAsync(overwriteRemote: true);
        }

        private async Task<AbroAiSyncResult> SyncInternalAsync(bool overwriteRemote)
        {
            CloudStorageCredentials credentials = null;
            LastError = null;
            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (!settings.AbroAiSyncEnabled)
                    return AbroAiSyncResult.Disabled;

                if (!TryResolveWebdav(settings, out string baseUrl, out string username, out string password))
                    return AbroAiSyncResult.NotConfigured;

                string remotePath = string.IsNullOrWhiteSpace(settings.AbroAiSyncRemotePath)
                    ? "ai.json"
                    : settings.AbroAiSyncRemotePath.Trim().TrimStart('/');

                (string folderUrl, string fileName) = SplitRemotePath(baseUrl, remotePath);
                await EnsureRemoteFolderAsync(baseUrl, folderUrl, username, password).ConfigureAwait(false);

                credentials = BuildCredentials(folderUrl, username, password);
                ICloudStorageClient client = _cloudStorageClientFactory.GetByKey(CloudStorageClientFactory.CloudStorageIdWebdav);

                if (!overwriteRemote)
                {
                    AbroAiStoreModel remote = new AbroAiStoreModel();
                    bool exists = false;
                    try
                    {
                        exists = await client.ExistsFileAsync(fileName, credentials).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Missing remote folder / 404 often surfaces as ConnectionFailedException
                        // ("An error occured while connecting to the server"). Treat as no remote yet.
                        exists = false;
                        System.Diagnostics.Debug.WriteLine("AI sync ExistsFile: " + Describe(ex));
                    }

                    if (exists)
                    {
                        byte[] bytes = await client.DownloadFileAsync(fileName, credentials).ConfigureAwait(false);
                        remote = AbroAiStoreFormat.Parse(Encoding.UTF8.GetString(bytes));
                        if (remote == null)
                        {
                            LastError = "The AI store file on the server has an unexpected format.";
                            return AbroAiSyncResult.Failed;
                        }
                    }

                    _storeService.MergeFromRemote(remote);
                }

                AbroAiStoreModel local = _storeService.LoadOrDefault();
                string json = AbroAiStoreFormat.Serialize(local);
                await client.UploadFileAsync(fileName, Encoding.UTF8.GetBytes(json), credentials).ConfigureAwait(false);
                LastError = null;
                return AbroAiSyncResult.Success;
            }
            catch (Exception ex)
            {
                LastError = Describe(ex);
                return AbroAiSyncResult.Failed;
            }
            finally
            {
                credentials?.Dispose();
            }
        }

        /// <summary>
        /// Same credential precedence as WebDAV quick-save: File WebDAV → Booky → notes WebDAV.
        /// </summary>
        private static bool TryResolveWebdav(
            SettingsModel settings,
            out string url,
            out string username,
            out string password)
        {
            url = string.Empty;
            username = string.Empty;
            password = string.Empty;
            if (settings == null)
                return false;

            if (!string.IsNullOrWhiteSpace(settings.FileWebdavUrl))
            {
                url = settings.FileWebdavUrl.Trim();
                username = settings.FileWebdavUsername ?? string.Empty;
                password = settings.FileWebdavPassword ?? string.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(settings.BookySyncUrl))
            {
                url = settings.BookySyncUrl.Trim();
                username = settings.BookySyncUsername ?? string.Empty;
                password = settings.BookySyncPassword ?? string.Empty;
                return true;
            }

            if (settings.Credentials != null
                && string.Equals(
                    settings.Credentials.CloudStorageId,
                    CloudStorageClientFactory.CloudStorageIdWebdav,
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.Credentials.Url))
            {
                url = settings.Credentials.Url.Trim();
                username = settings.Credentials.Username ?? string.Empty;
                password = settings.Credentials.UnprotectedPassword ?? string.Empty;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates each WebDAV collection segment under the base URL (MKCOL) when the AI path
        /// uses a subfolder such as <c>abro/ai.json</c>.
        /// </summary>
        private static async Task EnsureRemoteFolderAsync(
            string baseUrl,
            string folderUrl,
            string username,
            string password)
        {
            if (string.IsNullOrWhiteSpace(folderUrl) || string.IsNullOrWhiteSpace(baseUrl))
                return;

            string basePart = baseUrl.Trim().TrimEnd('/');
            string folder = folderUrl.Trim().TrimEnd('/');
            if (folder.Length == 0 || string.Equals(folder, basePart, StringComparison.OrdinalIgnoreCase))
                return;
            if (!folder.StartsWith(basePart + "/", StringComparison.OrdinalIgnoreCase))
                return;

            string relative = folder.Substring(basePart.Length).Trim('/');
            if (relative.Length == 0)
                return;

            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                string built = basePart;
                foreach (string part in relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    built = built + "/" + part;
                    using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), built + "/");
                    if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password))
                    {
                        string token = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes((username ?? string.Empty) + ":" + (password ?? string.Empty)));
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
                    }

                    using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
                    // 201 created; 405/409 already exists — all fine
                    System.Diagnostics.Debug.WriteLine("AI sync MKCOL " + built + " → " + (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AI sync MKCOL failed: " + Describe(ex));
            }
        }

        private static string Describe(Exception ex)
        {
            if (ex == null)
                return "Unknown error";
            string msg = ex.Message ?? ex.GetType().Name;
            if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
                msg = msg + " (" + ex.InnerException.Message + ")";
            return msg;
        }

        private static (string FolderUrl, string FileName) SplitRemotePath(string baseUrl, string remotePath)
        {
            string basePart = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            string path = (remotePath ?? string.Empty).Trim().TrimStart('/');
            if (path.Length == 0)
                path = "ai.json";

            int lastSlash = path.LastIndexOf('/');
            string folder = (lastSlash >= 0) ? path.Substring(0, lastSlash) : string.Empty;
            string fileName = (lastSlash >= 0) ? path.Substring(lastSlash + 1) : path;
            string folderUrl = (folder.Length > 0) ? basePart + "/" + folder : basePart;
            return (folderUrl, fileName);
        }

        private static CloudStorageCredentials BuildCredentials(string folderUrl, string username, string password)
        {
            var credentials = new CloudStorageCredentials
            {
                CloudStorageId = CloudStorageClientFactory.CloudStorageIdWebdav,
                Url = folderUrl,
                Username = username,
            };
            credentials.UnprotectedPassword = password ?? string.Empty;
            return credentials;
        }
    }
}
