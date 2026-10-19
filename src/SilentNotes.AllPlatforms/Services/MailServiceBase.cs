// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using SilentNotes.Models;
using SilentNotes.Workers;
using VanillaCloudStorageClient;
using VanillaCloudStorageClient.CloudStorageProviders;
using VanillaCloudStorageClient.OAuth2;

namespace SilentNotes.Services
{
    /// <summary>
    /// Base implementation of <see cref="IMailService"/> supporting local encrypted account storage,
    /// provider auto-discovery (Gmail, Outlook, Yahoo, Mail.ru, Yandex, Rambler, iCloud, Fastmail, etc.),
    /// IMAP over SSL/STARTTLS fetching with MIME decoding, and SMTP over SSL/STARTTLS sending.
    /// </summary>
    public abstract class MailServiceBase : IMailService
    {
#if RELEASE
        private const string StoreFileName = "anote_mail_store.xml";
#else
        private const string StoreFileName = "anote_mail_store_dev.xml";
#endif

        private const string GooglePendingFileName = "google_oauth_pending.txt";

        private readonly IXmlFileService _xmlFileService;
        private readonly IDataProtectionService _dataProtectionService;
        private readonly ICloudStorageClientFactory _cloudStorageClientFactory;

        protected MailServiceBase(
            IXmlFileService xmlFileService,
            IDataProtectionService dataProtectionService,
            ICloudStorageClientFactory cloudStorageClientFactory = null)
        {
            _xmlFileService = xmlFileService;
            _dataProtectionService = dataProtectionService;
            _cloudStorageClientFactory = cloudStorageClientFactory;
        }

        /// <summary>Gets the directory path where the mail store is persisted.</summary>
        protected abstract string GetStorageDirectory();

        /// <inheritdoc/>
        public MailStoreModel LoadStore()
        {
            try
            {
                string dir = GetStorageDirectory();
                string filePath = Path.Combine(dir, StoreFileName);
                if (_xmlFileService.TryLoad(filePath, out XDocument xml))
                {
                    var store = XmlUtils.DeserializeFromXmlDocument<MailStoreModel>(xml);
                    if (store != null)
                    {
                        foreach (var acc in store.Accounts)
                        {
                            UnprotectSecret(acc.EncryptedPassword, value => acc.Password = value);
                            UnprotectSecret(acc.EncryptedRefreshToken, value => acc.RefreshToken = value);
                            UnprotectSecret(acc.EncryptedAccessToken, value => acc.AccessToken = value);
                        }
                        return store;
                    }
                }
            }
            catch (Exception)
            {
            }
            return new MailStoreModel();
        }

        /// <inheritdoc/>
        public bool SaveStore(MailStoreModel store)
        {
            if (store == null)
                return false;

            try
            {
                foreach (var acc in store.Accounts)
                {
                    acc.Password = NormalizeAppPassword(acc);
                    if (_dataProtectionService != null)
                    {
                        acc.EncryptedPassword = ProtectSecret(acc.Password);
                        acc.EncryptedRefreshToken = ProtectSecret(acc.RefreshToken);
                        acc.EncryptedAccessToken = ProtectSecret(acc.AccessToken);
                    }
                }

                string dir = GetStorageDirectory();
                Directory.CreateDirectory(dir);
                string filePath = Path.Combine(dir, StoreFileName);
                return _xmlFileService.TrySerializeAndSave(filePath, store);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public void ApplyProviderPreset(MailAccountModel account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.EmailAddress))
                return;

            string email = account.EmailAddress.Trim();
            int atIdx = email.IndexOf('@');
            if (atIdx <= 0 || atIdx >= email.Length - 1)
                return;

            if (string.IsNullOrWhiteSpace(account.Username))
                account.Username = email;

            string domain = email.Substring(atIdx + 1).ToLowerInvariant();
            switch (domain)
            {
                case "gmail.com":
                case "googlemail.com":
                    account.ImapHost = "imap.gmail.com";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.gmail.com";
                    account.SmtpPort = 465;
                    account.SmtpUseSsl = true;
                    break;

                case "outlook.com":
                case "hotmail.com":
                case "live.com":
                case "msn.com":
                    account.ImapHost = "outlook.office365.com";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.office365.com";
                    account.SmtpPort = 587;
                    account.SmtpUseSsl = true;
                    break;

                case "yahoo.com":
                case "ymail.com":
                    account.ImapHost = "imap.mail.yahoo.com";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.mail.yahoo.com";
                    account.SmtpPort = 465;
                    account.SmtpUseSsl = true;
                    break;

                case "icloud.com":
                case "me.com":
                case "mac.com":
                    account.ImapHost = "imap.mail.me.com";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.mail.me.com";
                    account.SmtpPort = 587;
                    account.SmtpUseSsl = true;
                    break;

                case "mail.ru":
                case "inbox.ru":
                case "list.ru":
                case "bk.ru":
                case "internet.ru":
                    account.ImapHost = "imap.mail.ru";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.mail.ru";
                    account.SmtpPort = 465;
                    account.SmtpUseSsl = true;
                    break;

                case "yandex.ru":
                case "yandex.com":
                case "ya.ru":
                    account.ImapHost = "imap.yandex.ru";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.yandex.ru";
                    account.SmtpPort = 465;
                    account.SmtpUseSsl = true;
                    break;

                case "rambler.ru":
                case "lenta.ru":
                case "autorambler.ru":
                case "myrambler.ru":
                    account.ImapHost = "imap.rambler.ru";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.rambler.ru";
                    account.SmtpPort = 465;
                    account.SmtpUseSsl = true;
                    break;

                case "fastmail.com":
                case "fastmail.fm":
                    account.ImapHost = "imap.fastmail.com";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.fastmail.com";
                    account.SmtpPort = 465;
                    account.SmtpUseSsl = true;
                    break;

                case "zoho.com":
                    account.ImapHost = "imap.zoho.com";
                    account.ImapPort = 993;
                    account.ImapUseSsl = true;
                    account.SmtpHost = "smtp.zoho.com";
                    account.SmtpPort = 465;
                    account.SmtpUseSsl = true;
                    break;

                default:
                    if (string.IsNullOrWhiteSpace(account.ImapHost))
                        account.ImapHost = "imap." + domain;
                    if (account.ImapPort <= 0)
                        account.ImapPort = 993;
                    if (string.IsNullOrWhiteSpace(account.SmtpHost))
                        account.SmtpHost = "smtp." + domain;
                    if (account.SmtpPort <= 0)
                        account.SmtpPort = 465;
                    break;
            }
        }

        /// <summary>Removes the spaces Google shows inside an app password.</summary>
        public static string NormalizeAppPassword(MailAccountModel account)
        {
            string password = account?.Password ?? string.Empty;
            if (!IsGoogleHost(account))
                return password;
            return password.Replace(" ", string.Empty).Replace("\u00a0", string.Empty).Trim();
        }

        /// <inheritdoc/>
        public string BuildGoogleMailAuthorizationUrl(string state, string codeVerifier)
        {
            IOAuth2CloudStorageClient client = RequireGoogleClient();
            string url = client.BuildAuthorizationRequestUrl(state, codeVerifier);
            return url
                + "&access_type=offline&prompt="
                + Uri.EscapeDataString("select_account consent")
                + "&include_granted_scopes=true";
        }

        /// <inheritdoc/>
        public void SaveGoogleOAuthPending(string state, string codeVerifier)
        {
            string dir = GetStorageDirectory();
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, GooglePendingFileName), (state ?? string.Empty) + "\n" + (codeVerifier ?? string.Empty));
        }

        /// <inheritdoc/>
        public async Task<MailAccountModel> FinishGoogleMailAuthorizationAsync(string redirectUrl, CancellationToken cancellationToken = default)
        {
            string path = Path.Combine(GetStorageDirectory(), GooglePendingFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException("Google sign-in expired. Start it again from Mail settings.");

            string[] parts = File.ReadAllText(path).Split('\n');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
                throw new InvalidOperationException("Google sign-in state is missing.");

            IOAuth2CloudStorageClient client = RequireGoogleClient();
            CloudStorageToken token = await client.FetchTokenAsync(redirectUrl, parts[0].Trim(), parts[1].Trim());
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
                throw new InvalidOperationException("Google sign-in was cancelled.");

            string email = await ReadGmailAddressAsync(token.AccessToken, cancellationToken);
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
            }

            var account = new MailAccountModel
            {
                EmailAddress = email,
                Username = email,
                DisplayName = email,
                AuthMode = MailAuthModes.Google,
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken ?? string.Empty,
                AccessTokenExpiryUtc = token.ExpiryDate ?? DateTime.UtcNow.AddHours(1),
            };
            ApplyProviderPreset(account);
            return account;
        }

        private async Task PrepareCredentialsAsync(MailAccountModel account, MailStoreModel store, CancellationToken ct)
        {
            if (account == null)
                return;

            account.Password = NormalizeAppPassword(account);
            if (!account.UsesGoogleOAuth)
            {
                account.AccessToken = string.Empty;
                return;
            }

            bool fresh = !string.IsNullOrEmpty(account.AccessToken)
                && account.AccessTokenExpiryUtc > DateTime.UtcNow.AddMinutes(2);
            if (fresh)
                return;

            if (string.IsNullOrEmpty(account.RefreshToken))
                throw new InvalidOperationException("Google sign-in expired. Sign in again from Mail settings.");

            IOAuth2CloudStorageClient client = RequireGoogleClient();
            CloudStorageToken refreshed = await client.RefreshTokenAsync(new CloudStorageToken
            {
                AccessToken = account.AccessToken,
                RefreshToken = account.RefreshToken,
            });
            account.AccessToken = refreshed.AccessToken;
            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                account.RefreshToken = refreshed.RefreshToken;
            account.AccessTokenExpiryUtc = refreshed.ExpiryDate ?? DateTime.UtcNow.AddHours(1);
            if (store != null)
                SaveStore(store);
        }

        private IOAuth2CloudStorageClient RequireGoogleClient()
        {
            if (_cloudStorageClientFactory == null)
                throw new InvalidOperationException("Google sign-in is not available.");
            return _cloudStorageClientFactory.CreateGoogleOAuthClient(GoogleCloudStorageClient.GmailScope);
        }

        private static async Task<string> ReadGmailAddressAsync(string accessToken, CancellationToken ct)
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://gmail.googleapis.com/gmail/v1/users/me/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Could not read the Google account address.");

            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("emailAddress", out JsonElement email) && !string.IsNullOrWhiteSpace(email.GetString()))
                return email.GetString().Trim();
            throw new InvalidOperationException("Google did not return the mailbox address.");
        }

        private static bool IsGoogleHost(MailAccountModel account)
        {
            string host = (account?.ImapHost ?? string.Empty) + " " + (account?.EmailAddress ?? string.Empty);
            return host.IndexOf("gmail", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("googlemail", StringComparison.OrdinalIgnoreCase) >= 0
                || host.IndexOf("google.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildXoauth2(string user, string accessToken)
        {
            string raw = "user=" + (user ?? string.Empty) + "\u0001auth=Bearer " + accessToken + "\u0001\u0001";
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        }

        private static bool IsAuthenticationFailure(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;
            return message.IndexOf("AUTHENTICATIONFAILED", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("Invalid credentials", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("535", StringComparison.Ordinal) >= 0
                || message.IndexOf("534", StringComparison.Ordinal) >= 0
                || message.IndexOf("NO ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ProtectSecret(string value)
        {
            if (_dataProtectionService == null || string.IsNullOrEmpty(value))
                return null;
            return _dataProtectionService.Protect(Encoding.UTF8.GetBytes(value));
        }

        private void UnprotectSecret(string encrypted, Action<string> assign)
        {
            if (string.IsNullOrEmpty(encrypted) || _dataProtectionService == null)
                return;
            try
            {
                byte[] raw = _dataProtectionService.Unprotect(encrypted);
                if (raw != null)
                    assign(Encoding.UTF8.GetString(raw));
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public async Task<MailOperationResult> TestConnectionAsync(
            MailAccountModel account,
            CancellationToken cancellationToken = default)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.ImapHost))
                return new MailOperationResult { Success = false, ErrorMessage = "IMAP host is required." };

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(20));

                await using var imap = await ImapMiniClient.ConnectAsync(
                    account.ImapHost.Trim(),
                    account.ImapPort > 0 ? account.ImapPort : 993,
                    account.ImapUseSsl,
                    cts.Token);

                await PrepareCredentialsAsync(account, null, cts.Token);
                string user = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;
                await imap.AuthenticateAsync(user?.Trim() ?? string.Empty, account.Password ?? string.Empty, account.AccessToken, cts.Token);
                await imap.LogoutAsync(cts.Token);

                return new MailOperationResult { Success = true };
            }
            catch (Exception ex)
            {
                return new MailOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <inheritdoc/>
        public async Task<MailOperationResult> SyncAccountAsync(
            MailAccountModel account,
            MailStoreModel store,
            int maxMessages = 40,
            CancellationToken cancellationToken = default)
        {
            if (account == null || store == null)
                return new MailOperationResult { Success = false, ErrorMessage = "Invalid account or store." };

            if (string.IsNullOrWhiteSpace(account.ImapHost))
                return new MailOperationResult { Success = false, ErrorMessage = "IMAP host is not configured." };

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(45));

                await using var imap = await ImapMiniClient.ConnectAsync(
                    account.ImapHost.Trim(),
                    account.ImapPort > 0 ? account.ImapPort : 993,
                    account.ImapUseSsl,
                    cts.Token);

                await PrepareCredentialsAsync(account, store, cts.Token);
                string user = string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username;
                await imap.AuthenticateAsync(user?.Trim() ?? string.Empty, account.Password ?? string.Empty, account.AccessToken, cts.Token);

                int existsCount = await imap.SelectMailboxAsync("INBOX", cts.Token);
                int added = 0;

                if (existsCount > 0)
                {
                    int startSeq = Math.Max(1, existsCount - maxMessages + 1);
                    for (int seq = existsCount; seq >= startSeq; seq--)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        var fetched = await imap.FetchMessageAsync(seq, cts.Token);
                        if (fetched == null)
                            continue;

                        string uidKey = !string.IsNullOrEmpty(fetched.Uid) ? fetched.Uid : "seq_" + seq + "_" + fetched.MessageId;
                        MailMessageModel existing = store.Messages.FirstOrDefault(m =>
                            m.AccountId == account.Id &&
                            ((!string.IsNullOrEmpty(fetched.Uid) && m.RemoteUid == fetched.Uid) ||
                             (!string.IsNullOrEmpty(fetched.MessageId) && m.MessageId == fetched.MessageId)));

                        if (existing != null)
                        {
                            existing.IsRead = existing.IsRead || fetched.IsSeen;
                            existing.IsStarred = existing.IsStarred || fetched.IsFlagged;
                            continue;
                        }

                        var msg = new MailMessageModel
                        {
                            AccountId = account.Id,
                            RemoteUid = uidKey,
                            MessageId = fetched.MessageId ?? string.Empty,
                            Folder = MailFolders.Inbox,
                            From = fetched.From ?? string.Empty,
                            To = string.IsNullOrWhiteSpace(fetched.To) ? account.EmailAddress : fetched.To,
                            Cc = fetched.Cc ?? string.Empty,
                            Subject = string.IsNullOrWhiteSpace(fetched.Subject) ? "(No subject)" : fetched.Subject,
                            BodyText = fetched.BodyText ?? string.Empty,
                            BodyHtml = fetched.BodyHtml ?? string.Empty,
                            Snippet = BuildSnippet(fetched.BodyText, fetched.BodyHtml),
                            DateUtc = fetched.DateUtc,
                            IsRead = fetched.IsSeen,
                            IsStarred = fetched.IsFlagged,
                            Attachments = fetched.Attachments ?? new List<MailAttachmentMetaModel>(),
                        };

                        store.Messages.Add(msg);
                        added++;
                    }
                }

                await imap.LogoutAsync(cts.Token);
                SaveStore(store);

                return new MailOperationResult
                {
                    Success = true,
                    FetchedCount = added,
                };
            }
            catch (Exception ex)
            {
                return new MailOperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                };
            }
        }

        /// <inheritdoc/>
        public async Task<MailOperationResult> SendMessageAsync(
            MailAccountModel account,
            MailMessageModel message,
            IReadOnlyList<(string FileName, byte[] Content)> attachments = null,
            CancellationToken cancellationToken = default)
        {
            if (account == null || message == null)
                return new MailOperationResult { Success = false, ErrorMessage = "Invalid account or message." };

            if (string.IsNullOrWhiteSpace(account.SmtpHost))
                return new MailOperationResult { Success = false, ErrorMessage = "SMTP host is not configured." };

            var recipients = CollectRecipients(message.To, message.Cc, message.Bcc);
            if (recipients.Count == 0)
                return new MailOperationResult { Success = false, ErrorMessage = "At least one recipient email address is required." };

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(35));

                await PrepareCredentialsAsync(account, null, cts.Token);
                string rawMime = BuildMimeMessage(account, message, attachments);

                await SmtpMiniClient.SendAsync(
                    account.SmtpHost.Trim(),
                    account.SmtpPort > 0 ? account.SmtpPort : 465,
                    account.SmtpUseSsl,
                    string.IsNullOrWhiteSpace(account.Username) ? account.EmailAddress : account.Username,
                    account.Password ?? string.Empty,
                    account.AccessToken,
                    ExtractAddressOnly(account.EmailAddress),
                    recipients,
                    rawMime,
                    cts.Token);

                return new MailOperationResult { Success = true };
            }
            catch (Exception ex)
            {
                return new MailOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        internal static string BuildSnippet(string bodyText, string bodyHtml)
        {
            string source = bodyText;
            if (string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(bodyHtml))
            {
                source = StripHtmlTags(bodyHtml);
            }
            if (string.IsNullOrWhiteSpace(source))
                return string.Empty;

            string singleLine = Regex.Replace(source, @"\s+", " ").Trim();
            return singleLine.Length <= 140 ? singleLine : singleLine.Substring(0, 140) + "…";
        }

        internal static string StripHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            string cleaned = Regex.Replace(html, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"</(p|div|li|tr|h[1-6])>", "\n", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<[^>]+>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(cleaned).Trim();
        }

        private static List<string> CollectRecipients(params string[] fields)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string field in fields)
            {
                if (string.IsNullOrWhiteSpace(field))
                    continue;
                foreach (string part in field.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string addr = ExtractAddressOnly(part);
                    if (!string.IsNullOrEmpty(addr) && addr.Contains('@') && seen.Add(addr))
                        list.Add(addr);
                }
            }
            return list;
        }

        private static string ExtractAddressOnly(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            var match = Regex.Match(raw, @"<([^>]+)>");
            if (match.Success)
                return match.Groups[1].Value.Trim();
            return raw.Trim().Trim('<', '>', '"', '\'');
        }

        private static string EncodeHeaderValue(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            bool asciiOnly = text.All(c => c >= 32 && c < 127);
            if (asciiOnly)
                return text;
            return "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "?=";
        }

        private static string BuildMimeMessage(
            MailAccountModel account,
            MailMessageModel message,
            IReadOnlyList<(string FileName, byte[] Content)> attachments)
        {
            var sb = new StringBuilder();
            string fromAddr = ExtractAddressOnly(account.EmailAddress);
            string fromHeader = string.IsNullOrWhiteSpace(account.DisplayName)
                ? fromAddr
                : string.Format(CultureInfo.InvariantCulture, "{0} <{1}>", EncodeHeaderValue(account.DisplayName), fromAddr);

            string msgId = string.IsNullOrWhiteSpace(message.MessageId)
                ? string.Format(CultureInfo.InvariantCulture, "<{0}@anote.local>", Guid.NewGuid().ToString("N"))
                : message.MessageId;

            sb.Append("From: ").Append(fromHeader).Append("\r\n");
            sb.Append("To: ").Append(message.To ?? string.Empty).Append("\r\n");
            if (!string.IsNullOrWhiteSpace(message.Cc))
                sb.Append("Cc: ").Append(message.Cc).Append("\r\n");
            sb.Append("Subject: ").Append(EncodeHeaderValue(message.Subject ?? string.Empty)).Append("\r\n");
            sb.Append("Date: ").Append(DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("Message-ID: ").Append(msgId).Append("\r\n");
            sb.Append("MIME-Version: 1.0\r\n");

            bool hasAttachments = attachments != null && attachments.Count > 0;
            if (!hasAttachments)
            {
                sb.Append("Content-Type: text/plain; charset=\"utf-8\"\r\n");
                sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
                sb.Append(WrapBase64(Encoding.UTF8.GetBytes(message.BodyText ?? string.Empty)));
                sb.Append("\r\n");
            }
            else
            {
                string boundary = "----=_aNotePart_" + Guid.NewGuid().ToString("N");
                sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n\r\n");

                sb.Append("--").Append(boundary).Append("\r\n");
                sb.Append("Content-Type: text/plain; charset=\"utf-8\"\r\n");
                sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
                sb.Append(WrapBase64(Encoding.UTF8.GetBytes(message.BodyText ?? string.Empty)));
                sb.Append("\r\n");

                foreach (var att in attachments)
                {
                    if (att.Content == null)
                        continue;
                    string safeName = string.IsNullOrWhiteSpace(att.FileName) ? "attachment.bin" : Path.GetFileName(att.FileName);
                    sb.Append("--").Append(boundary).Append("\r\n");
                    sb.Append("Content-Type: application/octet-stream; name=\"").Append(EncodeHeaderValue(safeName)).Append("\"\r\n");
                    sb.Append("Content-Disposition: attachment; filename=\"").Append(EncodeHeaderValue(safeName)).Append("\"\r\n");
                    sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
                    sb.Append(WrapBase64(att.Content));
                    sb.Append("\r\n");
                }

                sb.Append("--").Append(boundary).Append("--\r\n");
            }

            return sb.ToString();
        }

        private static string WrapBase64(byte[] bytes)
        {
            string b64 = Convert.ToBase64String(bytes);
            var sb = new StringBuilder(b64.Length + (b64.Length / 76) * 2 + 4);
            for (int i = 0; i < b64.Length; i += 76)
            {
                int len = Math.Min(76, b64.Length - i);
                sb.Append(b64, i, len).Append("\r\n");
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>
        /// Lightweight IMAP4rev1 client supporting implicit SSL and STARTTLS, RFC 2047 encoded words,
        /// quoted-printable / base64 bodies, and multipart messages.
        /// </summary>
        private sealed class ImapMiniClient : IAsyncDisposable
        {
            private readonly TcpClient _tcp;
            private Stream _stream;
            private int _tagCounter;

            private ImapMiniClient(TcpClient tcp, Stream stream)
            {
                _tcp = tcp;
                _stream = stream;
            }

            public static async Task<ImapMiniClient> ConnectAsync(
                string host,
                int port,
                bool useSsl,
                CancellationToken ct)
            {
                var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, ct);
                Stream netStream = tcp.GetStream();

                if (useSsl && port != 143)
                {
                    var ssl = new SslStream(netStream, false, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(host, null, System.Security.Authentication.SslProtocols.None, false);
                    netStream = ssl;
                }

                var client = new ImapMiniClient(tcp, netStream);
                string greeting = await client.ReadLineAsync(ct);
                if (greeting == null || !greeting.StartsWith("*", StringComparison.Ordinal))
                    throw new IOException("Invalid IMAP greeting from " + host);

                if (useSsl && port == 143)
                {
                    await client.ExecuteCommandAsync("STARTTLS", ct);
                    var ssl = new SslStream(netStream, false, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(host, null, System.Security.Authentication.SslProtocols.None, false);
                    client._stream = ssl;
                }

                return client;
            }

            public async Task AuthenticateAsync(string user, string password, string accessToken, CancellationToken ct)
            {
                if (!string.IsNullOrEmpty(accessToken))
                {
                    await ExecuteCommandAsync("AUTHENTICATE XOAUTH2 " + BuildXoauth2(user, accessToken), ct);
                    return;
                }

                try
                {
                    string cmd = "LOGIN " + QuoteImapString(user) + " " + QuoteImapString(password);
                    await ExecuteCommandAsync(cmd, ct);
                }
                catch (IOException ex) when (IsAuthenticationFailure(ex.Message))
                {
                    string plain = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0" + user + "\0" + (password ?? string.Empty)));
                    await ExecuteCommandAsync("AUTHENTICATE PLAIN " + plain, ct);
                }
            }

            public async Task<int> SelectMailboxAsync(string mailbox, CancellationToken ct)
            {
                List<string> lines = await ExecuteCommandAsync("SELECT " + QuoteImapString(mailbox), ct);
                int exists = 0;
                foreach (string line in lines)
                {
                    var m = Regex.Match(line, @"^\*\s+(\d+)\s+EXISTS", RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                        exists = n;
                }
                return exists;
            }

            public async Task<ParsedImapMessage> FetchMessageAsync(int seqNum, CancellationToken ct)
            {
                string tag = NextTag();
                await WriteLineAsync(tag + " FETCH " + seqNum.ToString(CultureInfo.InvariantCulture) + " (UID FLAGS BODY.PEEK[])", ct);

                var metaBuilder = new StringBuilder();
                var rawLiteral = new MemoryStream();

                while (true)
                {
                    string line = await ReadLineAsync(ct);
                    if (line == null)
                        break;

                    if (line.StartsWith(tag + " ", StringComparison.Ordinal))
                    {
                        break;
                    }

                    metaBuilder.AppendLine(line);
                    var litMatch = Regex.Match(line, @"\{(\d+)\}\s*$");
                    if (litMatch.Success && int.TryParse(litMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int litBytes) && litBytes > 0)
                    {
                        byte[] buf = await ReadExactBytesAsync(litBytes, ct);
                        rawLiteral.Write(buf, 0, buf.Length);
                    }
                }

                byte[] rawMessageBytes = rawLiteral.ToArray();
                if (rawMessageBytes.Length == 0)
                    return null;

                string meta = metaBuilder.ToString();
                var uidMatch = Regex.Match(meta, @"\bUID\s+(\d+)", RegexOptions.IgnoreCase);
                string uid = uidMatch.Success ? uidMatch.Groups[1].Value : seqNum.ToString(CultureInfo.InvariantCulture);
                bool seen = Regex.IsMatch(meta, @"\\Seen", RegexOptions.IgnoreCase);
                bool flagged = Regex.IsMatch(meta, @"\\Flagged", RegexOptions.IgnoreCase);

                var parsed = MimeMiniParser.Parse(rawMessageBytes);
                parsed.Uid = uid;
                parsed.IsSeen = seen;
                parsed.IsFlagged = flagged;
                return parsed;
            }

            public async Task LogoutAsync(CancellationToken ct)
            {
                try
                {
                    await ExecuteCommandAsync("LOGOUT", ct);
                }
                catch (Exception)
                {
                }
            }

            private async Task<List<string>> ExecuteCommandAsync(string command, CancellationToken ct)
            {
                string tag = NextTag();
                await WriteLineAsync(tag + " " + command, ct);
                var lines = new List<string>();
                while (true)
                {
                    string line = await ReadLineAsync(ct);
                    if (line == null)
                        throw new IOException("Connection closed by IMAP server.");

                    if (line.StartsWith(tag + " ", StringComparison.Ordinal))
                    {
                        if (!line.Substring(tag.Length + 1).StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                            throw new IOException("IMAP error: " + line.Substring(tag.Length + 1));
                        break;
                    }
                    lines.Add(line);
                }
                return lines;
            }

            private string NextTag()
            {
                _tagCounter++;
                return "A" + _tagCounter.ToString("D4", CultureInfo.InvariantCulture);
            }

            private static string QuoteImapString(string value)
            {
                string escaped = (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
                return "\"" + escaped + "\"";
            }

            private async Task WriteLineAsync(string line, CancellationToken ct)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                await _stream.WriteAsync(bytes, 0, bytes.Length, ct);
                await _stream.FlushAsync(ct);
            }

            private async Task<string> ReadLineAsync(CancellationToken ct)
            {
                using var ms = new MemoryStream();
                byte[] one = new byte[1];
                while (true)
                {
                    int read = await _stream.ReadAsync(one, 0, 1, ct);
                    if (read == 0)
                        return ms.Length == 0 ? null : Encoding.UTF8.GetString(ms.ToArray());

                    if (one[0] == (byte)'\n')
                        break;
                    if (one[0] != (byte)'\r')
                        ms.WriteByte(one[0]);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }

            private async Task<byte[]> ReadExactBytesAsync(int count, CancellationToken ct)
            {
                byte[] buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int read = await _stream.ReadAsync(buffer, offset, count - offset, ct);
                    if (read <= 0)
                        break;
                    offset += read;
                }
                return buffer;
            }

            public ValueTask DisposeAsync()
            {
                try { _stream?.Dispose(); } catch (Exception) { }
                try { _tcp?.Dispose(); } catch (Exception) { }
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>
        /// Lightweight SMTP client supporting implicit TLS (465) and STARTTLS (587) with AUTH LOGIN / PLAIN.
        /// </summary>
        private static class SmtpMiniClient
        {
            public static async Task SendAsync(
                string host,
                int port,
                bool useSsl,
                string username,
                string password,
                string accessToken,
                string fromAddress,
                IReadOnlyList<string> recipients,
                string rawMimeMessage,
                CancellationToken ct)
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, ct);
                Stream stream = tcp.GetStream();

                if (useSsl && port == 465)
                {
                    var ssl = new SslStream(stream, false, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(host, null, System.Security.Authentication.SslProtocols.None, false);
                    stream = ssl;
                }

                await ExpectCodeAsync(stream, 220, ct);
                string ehloResp = await SendCommandAsync(stream, "EHLO anote.local", 250, ct);

                if (useSsl && port != 465 && ehloResp.IndexOf("STARTTLS", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    await SendCommandAsync(stream, "STARTTLS", 220, ct);
                    var ssl = new SslStream(stream, false, (_, _, _, _) => true);
                    await ssl.AuthenticateAsClientAsync(host, null, System.Security.Authentication.SslProtocols.None, false);
                    stream = ssl;
                    await SendCommandAsync(stream, "EHLO anote.local", 250, ct);
                }

                if (!string.IsNullOrEmpty(accessToken))
                {
                    await SendCommandAsync(stream, "AUTH XOAUTH2 " + BuildXoauth2(username.Trim(), accessToken), 235, ct);
                }
                else if (!string.IsNullOrWhiteSpace(username))
                {
                    try
                    {
                        await SendCommandAsync(stream, "AUTH LOGIN", 334, ct);
                        await SendCommandAsync(stream, Convert.ToBase64String(Encoding.UTF8.GetBytes(username.Trim())), 334, ct);
                        await SendCommandAsync(stream, Convert.ToBase64String(Encoding.UTF8.GetBytes(password ?? string.Empty)), 235, ct);
                    }
                    catch (IOException ex) when (IsAuthenticationFailure(ex.Message))
                    {
                        string plain = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0" + username.Trim() + "\0" + (password ?? string.Empty)));
                        await SendCommandAsync(stream, "AUTH PLAIN " + plain, 235, ct);
                    }
                }

                await SendCommandAsync(stream, "MAIL FROM:<" + fromAddress + ">", 250, ct);
                foreach (string rcpt in recipients)
                {
                    await SendCommandAsync(stream, "RCPT TO:<" + rcpt + ">", 250, ct);
                }

                await SendCommandAsync(stream, "DATA", 354, ct);

                // Dot-stuff lines starting with '.'
                string normalized = rawMimeMessage.Replace("\r\n", "\n").Replace("\r", "\n");
                var dataSb = new StringBuilder();
                foreach (string line in normalized.Split('\n'))
                {
                    if (line.StartsWith(".", StringComparison.Ordinal))
                        dataSb.Append('.');
                    dataSb.Append(line).Append("\r\n");
                }
                dataSb.Append(".\r\n");

                byte[] payload = Encoding.UTF8.GetBytes(dataSb.ToString());
                await stream.WriteAsync(payload, 0, payload.Length, ct);
                await stream.FlushAsync(ct);
                await ExpectCodeAsync(stream, 250, ct);

                try
                {
                    await WriteLineAsync(stream, "QUIT", ct);
                }
                catch (Exception)
                {
                }
            }

            private static async Task<string> SendCommandAsync(Stream stream, string command, int expectedCode, CancellationToken ct)
            {
                await WriteLineAsync(stream, command, ct);
                return await ExpectCodeAsync(stream, expectedCode, ct);
            }

            private static async Task<string> ExpectCodeAsync(Stream stream, int expectedCode, CancellationToken ct)
            {
                var sb = new StringBuilder();
                while (true)
                {
                    string line = await ReadLineAsync(stream, ct);
                    if (line == null)
                        throw new IOException("SMTP connection closed unexpectedly.");
                    sb.AppendLine(line);
                    if (line.Length >= 3 && int.TryParse(line.Substring(0, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                    {
                        if (line.Length == 3 || line[3] == ' ')
                        {
                            if (code != expectedCode && !(expectedCode == 250 && code == 251))
                                throw new IOException("SMTP error: " + line);
                            return sb.ToString();
                        }
                    }
                }
            }

            private static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                await stream.WriteAsync(bytes, 0, bytes.Length, ct);
                await stream.FlushAsync(ct);
            }

            private static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct)
            {
                using var ms = new MemoryStream();
                byte[] one = new byte[1];
                while (true)
                {
                    int read = await stream.ReadAsync(one, 0, 1, ct);
                    if (read == 0)
                        return ms.Length == 0 ? null : Encoding.UTF8.GetString(ms.ToArray());
                    if (one[0] == (byte)'\n')
                        break;
                    if (one[0] != (byte)'\r')
                        ms.WriteByte(one[0]);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        internal sealed class ParsedImapMessage
        {
            public string Uid { get; set; }
            public string MessageId { get; set; }
            public string From { get; set; }
            public string To { get; set; }
            public string Cc { get; set; }
            public string Subject { get; set; }
            public DateTime DateUtc { get; set; } = DateTime.UtcNow;
            public string BodyText { get; set; } = string.Empty;
            public string BodyHtml { get; set; } = string.Empty;
            public bool IsSeen { get; set; }
            public bool IsFlagged { get; set; }
            public List<MailAttachmentMetaModel> Attachments { get; set; } = new List<MailAttachmentMetaModel>();
        }

        /// <summary>
        /// Parses RFC 5322 / MIME messages (headers, RFC 2047 encoded words, quoted-printable, base64,
        /// and multipart/alternative or multipart/mixed parts).
        /// </summary>
        private static class MimeMiniParser
        {
            public static ParsedImapMessage Parse(byte[] rawBytes)
            {
                var result = new ParsedImapMessage();
                string rawText = Encoding.Latin1.GetString(rawBytes);

                var (headers, bodyPart) = SplitHeadersAndBody(rawText);
                result.From = DecodeMimeWords(headers.GetValueOrDefault("from", string.Empty));
                result.To = DecodeMimeWords(headers.GetValueOrDefault("to", string.Empty));
                result.Cc = DecodeMimeWords(headers.GetValueOrDefault("cc", string.Empty));
                result.Subject = DecodeMimeWords(headers.GetValueOrDefault("subject", string.Empty));
                result.MessageId = headers.GetValueOrDefault("message-id", string.Empty).Trim();

                if (headers.TryGetValue("date", out string dateStr) && !string.IsNullOrWhiteSpace(dateStr))
                {
                    string cleanedDate = Regex.Replace(dateStr, @"\s*\([^)]*\)\s*$", string.Empty).Trim();
                    if (DateTimeOffset.TryParse(cleanedDate, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset dto))
                        result.DateUtc = dto.UtcDateTime;
                }

                ParseMimeEntity(headers, bodyPart, result, 0);

                if (string.IsNullOrWhiteSpace(result.BodyText) && !string.IsNullOrWhiteSpace(result.BodyHtml))
                    result.BodyText = StripHtmlTags(result.BodyHtml);

                return result;
            }

            private static void ParseMimeEntity(
                Dictionary<string, string> headers,
                string bodyRaw,
                ParsedImapMessage target,
                int depth)
            {
                if (depth > 8)
                    return;

                string contentType = headers.GetValueOrDefault("content-type", "text/plain");
                string transferEncoding = headers.GetValueOrDefault("content-transfer-encoding", "7bit").Trim().ToLowerInvariant();
                string disposition = headers.GetValueOrDefault("content-disposition", string.Empty);

                if (contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                {
                    string boundary = ExtractParameter(contentType, "boundary");
                    if (!string.IsNullOrEmpty(boundary))
                    {
                        string delim = "--" + boundary;
                        string[] parts = bodyRaw.Split(new[] { delim }, StringSplitOptions.None);
                        for (int i = 1; i < parts.Length; i++)
                        {
                            string part = parts[i];
                            if (part.StartsWith("--", StringComparison.Ordinal))
                                break;
                            part = part.TrimStart('\r', '\n');
                            var (subHeaders, subBody) = SplitHeadersAndBody(part);
                            ParseMimeEntity(subHeaders, subBody, target, depth + 1);
                        }
                        return;
                    }
                }

                string fileName = ExtractParameter(disposition, "filename");
                if (string.IsNullOrEmpty(fileName))
                    fileName = ExtractParameter(contentType, "name");

                bool isAttachment = disposition.StartsWith("attachment", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(fileName) && !contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase));

                if (isAttachment)
                {
                    byte[] decodedBytes = DecodeBodyBytes(bodyRaw, transferEncoding);
                    target.Attachments.Add(new MailAttachmentMetaModel
                    {
                        FileName = DecodeMimeWords(string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName),
                        ContentType = contentType.Split(';')[0].Trim(),
                        SizeBytes = decodedBytes.Length,
                    });
                    return;
                }

                Encoding charset = ResolveEncoding(ExtractParameter(contentType, "charset"));
                byte[] contentBytes = DecodeBodyBytes(bodyRaw, transferEncoding);
                string decodedText = charset.GetString(contentBytes).TrimEnd('\r', '\n', ' ');

                if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(target.BodyHtml))
                        target.BodyHtml = decodedText;
                }
                else if (contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(target.BodyText))
                {
                    if (string.IsNullOrEmpty(target.BodyText))
                        target.BodyText = decodedText;
                }
            }

            private static (Dictionary<string, string> Headers, string Body) SplitHeadersAndBody(string raw)
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int splitIdx = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                int sepLen = 4;
                if (splitIdx < 0)
                {
                    splitIdx = raw.IndexOf("\n\n", StringComparison.Ordinal);
                    sepLen = 2;
                }

                string headerBlock = splitIdx >= 0 ? raw.Substring(0, splitIdx) : raw;
                string bodyBlock = splitIdx >= 0 ? raw.Substring(splitIdx + sepLen) : string.Empty;

                string unfolded = Regex.Replace(headerBlock, @"\r?\n[ \t]+", " ");
                foreach (string line in unfolded.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string key = line.Substring(0, colon).Trim();
                        string val = line.Substring(colon + 1).Trim();
                        if (!headers.ContainsKey(key))
                            headers[key] = val;
                    }
                }

                return (headers, bodyBlock);
            }

            private static string ExtractParameter(string headerValue, string paramName)
            {
                if (string.IsNullOrEmpty(headerValue))
                    return null;
                var match = Regex.Match(
                    headerValue,
                    paramName + @"\s*=\s*(?:""([^""]*)""|([^;\s]+))",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;
                return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            }

            private static byte[] DecodeBodyBytes(string bodyRaw, string transferEncoding)
            {
                if (transferEncoding == "base64")
                {
                    try
                    {
                        string cleaned = Regex.Replace(bodyRaw, @"\s+", string.Empty);
                        return Convert.FromBase64String(cleaned);
                    }
                    catch (Exception)
                    {
                        return Encoding.UTF8.GetBytes(bodyRaw);
                    }
                }

                if (transferEncoding == "quoted-printable")
                {
                    return DecodeQuotedPrintable(bodyRaw);
                }

                return Encoding.Latin1.GetBytes(bodyRaw);
            }

            private static byte[] DecodeQuotedPrintable(string input)
            {
                using var ms = new MemoryStream();
                for (int i = 0; i < input.Length; i++)
                {
                    char c = input[i];
                    if (c == '=')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '\n')
                        {
                            i += 1;
                            continue;
                        }
                        if (i + 2 < input.Length && input[i + 1] == '\r' && input[i + 2] == '\n')
                        {
                            i += 2;
                            continue;
                        }
                        if (i + 2 < input.Length &&
                            byte.TryParse(input.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                        {
                            ms.WriteByte(b);
                            i += 2;
                            continue;
                        }
                    }
                    ms.WriteByte((byte)c);
                }
                return ms.ToArray();
            }

            internal static string DecodeMimeWords(string input)
            {
                if (string.IsNullOrEmpty(input) || !input.Contains("=?", StringComparison.Ordinal))
                    return input ?? string.Empty;

                // Collapse whitespace between adjacent encoded words
                string collapsed = Regex.Replace(input, @"(\?=)\s+(=\?)", "$1$2");
                return Regex.Replace(collapsed, @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=", m =>
                {
                    try
                    {
                        Encoding enc = ResolveEncoding(m.Groups[1].Value);
                        string mode = m.Groups[2].Value.ToUpperInvariant();
                        string payload = m.Groups[3].Value;
                        if (mode == "B")
                        {
                            return enc.GetString(Convert.FromBase64String(payload));
                        }
                        else
                        {
                            string qp = payload.Replace('_', ' ');
                            return enc.GetString(DecodeQuotedPrintable(qp));
                        }
                    }
                    catch (Exception)
                    {
                        return m.Value;
                    }
                });
            }

            private static Encoding ResolveEncoding(string charset)
            {
                if (string.IsNullOrWhiteSpace(charset))
                    return Encoding.UTF8;
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return Encoding.GetEncoding(charset.Trim().Trim('"', '\''));
                }
                catch (Exception)
                {
                    return Encoding.UTF8;
                }
            }
        }
    }
}
