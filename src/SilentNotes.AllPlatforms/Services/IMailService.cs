// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Result of an IMAP sync or SMTP send operation.
    /// </summary>
    public class MailOperationResult
    {
        public bool Success { get; set; }
        public int FetchedCount { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Service for managing email accounts, local mail storage, IMAP fetching, and SMTP sending.
    /// </summary>
    public interface IMailService
    {
        /// <summary>Loads the stored accounts and messages from local storage.</summary>
        MailStoreModel LoadStore();

        /// <summary>Saves the mail store (accounts and messages) to local storage.</summary>
        bool SaveStore(MailStoreModel store);

        /// <summary>Guesses IMAP/SMTP host and port settings from an email address.</summary>
        void ApplyProviderPreset(MailAccountModel account);

        /// <summary>Tests the IMAP and SMTP connection for the given account.</summary>
        Task<MailOperationResult> TestConnectionAsync(MailAccountModel account, CancellationToken cancellationToken = default);

        /// <summary>Fetches recent emails from the IMAP server and merges them into the store.</summary>
        Task<MailOperationResult> SyncAccountAsync(
            MailAccountModel account,
            MailStoreModel store,
            int maxMessages = 40,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds the Google authorization URL for IMAP/SMTP. The account picker is shown so a
        /// mailbox other than the note-sync account can be chosen.
        /// </summary>
        string BuildGoogleMailAuthorizationUrl(string state, string codeVerifier);

        /// <summary>Persists the PKCE state until the browser redirects back.</summary>
        void SaveGoogleOAuthPending(string state, string codeVerifier);

        /// <summary>Exchanges the redirect for tokens and returns a Gmail account.</summary>
        Task<MailAccountModel> FinishGoogleMailAuthorizationAsync(string redirectUrl, CancellationToken cancellationToken = default);

        /// <summary>Sends an email message via the account's SMTP server.</summary>
        Task<MailOperationResult> SendMessageAsync(
            MailAccountModel account,
            MailMessageModel message,
            IReadOnlyList<(string FileName, byte[] Content)> attachments = null,
            CancellationToken cancellationToken = default);
    }
}
