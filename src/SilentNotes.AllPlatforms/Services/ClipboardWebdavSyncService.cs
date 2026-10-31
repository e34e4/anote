// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;
using VanillaCloudStorageClient;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class ClipboardWebdavSyncService : IClipboardWebdavSyncService
    {
        private const int DebounceMs = 3000;
        private const int MaxTextItems = 100;

        private readonly IClipboardHistoryService _historyService;
        private readonly IWebdavFileService _webdavService;
        private readonly ISettingsService _settingsService;
        private readonly object _timerLock = new object();
        private CancellationTokenSource _debounceCts;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardWebdavSyncService"/> class.
        /// </summary>
        public ClipboardWebdavSyncService(
            IClipboardHistoryService historyService,
            IWebdavFileService webdavService,
            ISettingsService settingsService,
            IMessengerService messengerService)
        {
            _historyService = historyService;
            _webdavService = webdavService;
            _settingsService = settingsService;

            messengerService?.Register<ClipboardChangedMessage>(this, (recipient, message) => ScheduleUpload());
        }

        /// <inheritdoc/>
        public string LastError { get; private set; }

        /// <inheritdoc/>
        public bool IsConfigured
        {
            get
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (!settings.ClipboardWebdavSyncEnabled)
                    return false;
                return ResolveCredentials(out _, out _, out _);
            }
        }

        /// <inheritdoc/>
        public void ScheduleUpload()
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            if (!settings.ClipboardWebdavSyncEnabled)
                return;

            CancellationToken token;
            lock (_timerLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                token = _debounceCts.Token;
            }

            _ = DebouncedUploadAsync(token);
        }

        private async Task DebouncedUploadAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(DebounceMs, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    return;
                await UploadNowAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private async Task UploadNowAsync()
        {
            LastError = null;
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
            if (!settings.ClipboardWebdavSyncEnabled)
                return;

            if (!ResolveCredentials(out string url, out string username, out string password))
            {
                LastError = "WebDAV is not configured.";
                return;
            }

            try
            {
                byte[] content = BuildSyncFileBytes();
                await EnsureFolderExistsAsync(url, IClipboardWebdavSyncService.RemoteFolderName, username, password)
                    .ConfigureAwait(false);
                await _webdavService.UploadFileAsync(
                    url,
                    IClipboardWebdavSyncService.RemoteFolderName,
                    IClipboardWebdavSyncService.RemoteFileName,
                    content,
                    username,
                    password).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private byte[] BuildSyncFileBytes()
        {
            ClipboardHistoryModel history = _historyService.LoadHistoryOrDefault();
            var items = new List<ClipboardSyncItemDto>();
            foreach (ClipboardItemModel item in history.Items
                .Where(i => !i.IsDeleted && i.Kind == ClipboardItemKind.Text && !string.IsNullOrEmpty(i.Text))
                .Take(MaxTextItems))
            {
                items.Add(new ClipboardSyncItemDto
                {
                    Id = item.Id.ToString("D"),
                    CreatedUtc = ToIso(item.CreatedUtc),
                    ModifiedUtc = ToIso(item.ModifiedUtc),
                    Kind = "text",
                    Hash = item.ContentHash,
                    Text = item.Text,
                });
            }

            var payload = new ClipboardSyncFileDto
            {
                UpdatedUtc = ToIso(DateTime.UtcNow),
                Items = items,
            };

            string json = JsonSerializer.Serialize(payload, CreateOptions());
            return Encoding.UTF8.GetBytes(json);
        }

        private async Task EnsureFolderExistsAsync(string url, string remoteFolder, string username, string password)
        {
            string[] parts = (remoteFolder ?? string.Empty).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            string built = string.Empty;
            foreach (string part in parts)
            {
                string parent = built;
                try
                {
                    await _webdavService.CreateDirectoryAsync(url, parent, part, username, password)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
                built = string.IsNullOrEmpty(built) ? part : built + "/" + part;
            }
        }

        private bool ResolveCredentials(out string url, out string username, out string password)
        {
            SettingsModel settings = _settingsService.LoadSettingsOrDefault();
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

            if (settings.Credentials != null &&
                string.Equals(settings.Credentials.CloudStorageId, CloudStorageClientFactory.CloudStorageIdWebdav, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(settings.Credentials.Url))
            {
                url = settings.Credentials.Url.Trim();
                username = settings.Credentials.Username ?? string.Empty;
                password = settings.Credentials.UnprotectedPassword ?? string.Empty;
                return true;
            }

            url = string.Empty;
            username = string.Empty;
            password = string.Empty;
            return false;
        }

        private static JsonSerializerOptions CreateOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        private static string ToIso(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private sealed class ClipboardSyncFileDto
        {
            [JsonPropertyName("updated_utc")]
            public string UpdatedUtc { get; set; }

            [JsonPropertyName("items")]
            public List<ClipboardSyncItemDto> Items { get; set; }
        }

        private sealed class ClipboardSyncItemDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("created_utc")]
            public string CreatedUtc { get; set; }

            [JsonPropertyName("modified_utc")]
            public string ModifiedUtc { get; set; }

            [JsonPropertyName("kind")]
            public string Kind { get; set; }

            [JsonPropertyName("hash")]
            public string Hash { get; set; }

            [JsonPropertyName("text")]
            public string Text { get; set; }
        }
    }
}
