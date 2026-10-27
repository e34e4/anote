// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Multi-account Telegram MTProto client used by the TG tab.
    /// Privacy rules: never send typing or online presence; re-assert offline after connect;
    /// never read stories; mark history read only when the user interacts.
    /// </summary>
    public interface ITelegramService : IAsyncDisposable, IDisposable
    {
        /// <summary>Raised when the chat list for the active (or any) account changes.</summary>
        event Action ChatsChanged;

        /// <summary>Raised when messages in an open chat change. Argument is the chat key.</summary>
        event Action<string> MessagesChanged;

        /// <summary>Raised when authentication state changes for an account or pending login.</summary>
        event Action AuthChanged;

        /// <summary>Raised when an incoming message mentions us or replies to our message.</summary>
        event Action<TelegramMessageModel> MentionOrReplyReceived;

        /// <summary>Loads the encrypted account store from app-private storage.</summary>
        TelegramStoreModel LoadStore();

        /// <summary>Saves the account store (API credentials + accounts) to app-private storage.</summary>
        bool SaveStore(TelegramStoreModel store);

        /// <summary>Creates cache and download directories under the external tg root.</summary>
        void EnsureDirectories();

        /// <summary>Gets the auth/connection state for an account, or the pending login flow when accountId is null.</summary>
        TelegramAuthState GetAuthState(string accountId = null);

        /// <summary>Starts adding a new account (requires API credentials in the store).</summary>
        Task<TelegramOperationResult> StartAddAccountAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts QR login (Desktop-style). Official Telegram → Settings → Devices → Link Desktop Device
        /// scans the QR. Preferred because third-party clients rarely receive SMS codes.
        /// </summary>
        Task<TelegramOperationResult> StartQrLoginAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Continues the login flow. Pass the value required by the current auth state
        /// (phone, verification code, or 2FA password). Unused parameters may be null.
        /// </summary>
        Task<TelegramOperationResult> ContinueLoginAsync(
            string phone = null,
            string code = null,
            string password = null,
            CancellationToken cancellationToken = default);

        /// <summary>Cancels an in-progress add-account login and disposes the pending client.</summary>
        Task CancelLoginAsync();

        /// <summary>
        /// Asks Telegram to resend the verification code via the next delivery method
        /// (official app → SMS/call when available). Only valid while waiting for a code.
        /// </summary>
        Task<TelegramOperationResult> ResendVerificationCodeAsync(CancellationToken cancellationToken = default);

        /// <summary>Latest hint about where the login code was sent (official app, SMS, call, …).</summary>
        string LastCodeDeliveryHint { get; }

        /// <summary>Current <c>tg://login?token=…</c> URL while <see cref="TelegramAuthState.NeedQr"/>.</summary>
        string LastQrLoginUrl { get; }

        /// <summary>Opens the official Telegram app (manual action; does not cancel login).</summary>
        void OpenOfficialTelegramApp();

        /// <summary>Opens official Telegram’s “Link Desktop Device” / Devices screen when possible.</summary>
        void OpenOfficialTelegramDevices();

        /// <summary>Connects (or resumes) an existing account session.</summary>
        Task<TelegramOperationResult> ConnectAccountAsync(string accountId, CancellationToken cancellationToken = default);

        /// <summary>Disconnects an account client without removing it from the store.</summary>
        Task DisconnectAccountAsync(string accountId);

        /// <summary>Sets the active account id in the store and raises auth/chat events.</summary>
        Task<TelegramOperationResult> SwitchActiveAccountAsync(string accountId);

        /// <summary>Removes an account from the store and deletes its session file.</summary>
        Task<TelegramOperationResult> RemoveAccountAsync(string accountId);

        /// <summary>Returns the cached chat list for an account (active account when null).</summary>
        IReadOnlyList<TelegramChatModel> GetChats(string accountId = null);

        /// <summary>Refreshes the dialog list from Telegram.</summary>
        Task<TelegramOperationResult> RefreshChatsAsync(string accountId = null, CancellationToken cancellationToken = default);

        /// <summary>Opens a chat for viewing (loads recent messages). Does not mark as read by itself.</summary>
        Task<TelegramOperationResult> OpenChatAsync(string chatKey, string accountId = null, int limit = 40, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves a Telegram deep link (<c>https://t.me/…</c>, <c>tg://…</c>, or Mini App path)
        /// and opens the matching chat. Sets <see cref="TelegramOperationResult.ChatKey"/> on success.
        /// </summary>
        Task<TelegramOperationResult> OpenTelegramLinkAsync(
            string linkOrPath,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads an older page of messages for the open chat (earlier than the oldest currently cached).
        /// Appends them after the existing buffer (newest-first UI → older messages toward the bottom).
        /// </summary>
        Task<TelegramOperationResult> LoadOlderMessagesAsync(
            string chatKey,
            string accountId = null,
            int limit = 40,
            CancellationToken cancellationToken = default);

        /// <summary>True when the open chat may still have older messages to load.</summary>
        bool HasMoreMessages(string chatKey = null, string accountId = null);

        /// <summary>Closes the currently open chat.</summary>
        void CloseChat(string accountId = null);

        /// <summary>Returns cached messages for the open chat (or the given chat key).</summary>
        IReadOnlyList<TelegramMessageModel> GetMessages(string chatKey = null, string accountId = null);

        /// <summary>Forwards a message. When <paramref name="dropAuthor"/> is true, forwards without sender name/quote.</summary>
        Task<TelegramOperationResult> ForwardMessageAsync(
            string fromChatKey,
            int messageId,
            string toChatKey,
            bool dropAuthor,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Deletes a message for me only (revoke=false). Channels: best-effort Channels_DeleteMessages.</summary>
        Task<TelegramOperationResult> DeleteMessageForMeAsync(
            string chatKey,
            int messageId,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Edits the text of an outgoing message.</summary>
        Task<TelegramOperationResult> EditMessageTextAsync(
            string chatKey,
            int messageId,
            string newText,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Sends a 👍 reaction (or clears it when already chosen).</summary>
        Task<TelegramOperationResult> ToggleLikeReactionAsync(
            string chatKey,
            int messageId,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a text message. When <paramref name="format"/> includes <see cref="TelegramTextFormat.MarkdownLite"/>
        /// (default), markdown-lite markers are parsed into entities. Otherwise WTelegram MarkdownV2 helpers may be used
        /// when <paramref name="useTelegramMarkdownV2"/> is true.
        /// <paramref name="silent"/> defaults to true (no notification sound for recipients).
        /// </summary>
        Task<TelegramOperationResult> SendTextMessageAsync(
            string chatKey,
            string text,
            string accountId = null,
            int replyToMsgId = 0,
            TelegramTextFormat format = TelegramTextFormat.MarkdownLite,
            bool useTelegramMarkdownV2 = false,
            bool silent = true,
            CancellationToken cancellationToken = default);

        /// <summary>Uploads and sends a file as a document attachment. Silent by default.</summary>
        Task<TelegramOperationResult> SendFileAttachmentAsync(
            string chatKey,
            byte[] content,
            string fileName,
            string accountId = null,
            string caption = null,
            int replyToMsgId = 0,
            bool silent = true,
            CancellationToken cancellationToken = default);

        /// <summary>Uploads and sends an image as a photo attachment. Silent by default.</summary>
        Task<TelegramOperationResult> SendPhotoAttachmentAsync(
            string chatKey,
            byte[] content,
            string fileName,
            string accountId = null,
            string caption = null,
            int replyToMsgId = 0,
            bool silent = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads media referenced by <paramref name="mediaRef"/> into the external tg root
        /// (e.g. /sdcard/tg). Does not auto-download; only called explicitly.
        /// </summary>
        Task<TelegramOperationResult> DownloadMediaAsync(TelegramMediaRef mediaRef, CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles a bot button click. Callback buttons invoke GetBotCallbackAnswer;
        /// URL / WebApp buttons return the URL in <see cref="TelegramOperationResult.Url"/>.
        /// Reply-keyboard buttons are sent as a normal text message.
        /// </summary>
        Task<TelegramOperationResult> ClickBotButtonAsync(
            string chatKey,
            TelegramBotButtonModel button,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Returns a bot WebApp / URL button target for opening in the system browser.</summary>
        Task<TelegramOperationResult> OpenBotAppUrlAsync(
            string chatKey,
            TelegramBotButtonModel button,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a bot’s main Mini App / menu WebView (the chat-list “Open” button).
        /// Returns the authenticated webview URL in <see cref="TelegramOperationResult.Url"/>.
        /// </summary>
        Task<TelegramOperationResult> OpenBotMainAppAsync(
            string chatKey,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Pins or unpins dialogs in the main chat list.</summary>
        Task<TelegramOperationResult> SetChatsPinnedAsync(
            IReadOnlyList<string> chatKeys,
            bool pinned,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks the chat history as read. Call only when the user opens a chat or sends a message —
        /// never from background update handlers.
        /// </summary>
        Task MarkChatReadOnInteractAsync(string chatKey, string accountId = null, CancellationToken cancellationToken = default);

        /// <summary>Moves dialogs into (folder_id=1) or out of (folder_id=0) the Archived chats folder.</summary>
        Task<TelegramOperationResult> SetChatsArchivedAsync(
            IReadOnlyList<string> chatKeys,
            bool archived,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Deletes chat history for me only (does not revoke for other participants).</summary>
        Task<TelegramOperationResult> DeleteChatsForMeAsync(
            IReadOnlyList<string> chatKeys,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Mutes (forever) or unmutes notifications for the given chats.</summary>
        Task<TelegramOperationResult> SetChatsMutedAsync(
            IReadOnlyList<string> chatKeys,
            bool muted,
            string accountId = null,
            CancellationToken cancellationToken = default);

        /// <summary>Marks all selected chats as read (user-initiated).</summary>
        Task<TelegramOperationResult> MarkChatsReadAsync(
            IReadOnlyList<string> chatKeys,
            string accountId = null,
            CancellationToken cancellationToken = default);
    }
}
