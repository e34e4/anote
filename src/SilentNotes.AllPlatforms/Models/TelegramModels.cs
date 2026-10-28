// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializable Telegram store containing shared API credentials and user accounts.
    /// </summary>
    [XmlRoot("anote_telegram_store")]
    public class TelegramStoreModel
    {
        [XmlElement("api_id")]
        public int ApiId { get; set; }

        [XmlIgnore]
        public string ApiHash { get; set; } = string.Empty;

        [XmlElement("encrypted_api_hash")]
        public string EncryptedApiHash { get; set; }

        [XmlElement("active_account_id")]
        public string ActiveAccountId { get; set; } = string.Empty;

        [XmlArray("accounts")]
        [XmlArrayItem("account")]
        public List<TelegramAccountModel> Accounts { get; set; } = new List<TelegramAccountModel>();
    }

    /// <summary>
    /// A Telegram user account managed by aNote.
    /// </summary>
    public class TelegramAccountModel
    {
        public TelegramAccountModel()
        {
            Id = Guid.NewGuid().ToString("N");
        }

        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlElement("phone")]
        public string Phone { get; set; } = string.Empty;

        [XmlElement("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [XmlElement("username")]
        public string Username { get; set; } = string.Empty;

        [XmlElement("user_id")]
        public long UserId { get; set; }

        /// <summary>Session file name under the external tg/cache/sessions folder.</summary>
        [XmlElement("session_file")]
        public string SessionFileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// A dialog (private chat, group, channel, or bot) in the chat list.
    /// </summary>
    public class TelegramChatModel
    {
        /// <summary>Stable key such as "user:123", "chat:456", "channel:789".</summary>
        [XmlAttribute("id")]
        public string Id { get; set; } = string.Empty;

        [XmlElement("title")]
        public string Title { get; set; } = string.Empty;

        [XmlElement("is_bot")]
        public bool IsBot { get; set; }

        [XmlElement("is_channel")]
        public bool IsChannel { get; set; }

        [XmlElement("is_group")]
        public bool IsGroup { get; set; }

        [XmlElement("unread_count")]
        public int UnreadCount { get; set; }

        [XmlElement("last_preview")]
        public string LastMessagePreview { get; set; } = string.Empty;

        [XmlElement("last_at")]
        public DateTime LastMessageAt { get; set; }

        /// <summary>True when the dialog lives in Telegram’s Archived folder (folder_id = 1).</summary>
        [XmlElement("is_archived")]
        public bool IsArchived { get; set; }

        /// <summary>True when notifications are muted until a future time (or forever).</summary>
        [XmlElement("is_muted")]
        public bool IsMuted { get; set; }

        /// <summary>True when the dialog is pinned in the chat list.</summary>
        [XmlElement("is_pinned")]
        public bool IsPinned { get; set; }

        /// <summary>True when this bot exposes a Mini App / menu “Open” button.</summary>
        [XmlElement("has_bot_app")]
        public bool HasBotApp { get; set; }

        /// <summary>Label for the bot Open button (defaults to “Open”).</summary>
        [XmlElement("bot_app_label")]
        public string BotAppLabel { get; set; } = string.Empty;

        /// <summary>Peer kind: user, chat, or channel.</summary>
        [XmlElement("peer_type")]
        public string PeerType { get; set; } = TelegramPeerTypes.User;

        [XmlElement("peer_id")]
        public long PeerId { get; set; }
    }

    /// <summary>
    /// A single message in a Telegram chat.
    /// </summary>
    public class TelegramMessageModel
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlElement("chat_key")]
        public string ChatKey { get; set; } = string.Empty;

        [XmlElement("from_name")]
        public string FromName { get; set; } = string.Empty;

        [XmlElement("from_id")]
        public long FromId { get; set; }

        [XmlElement("text")]
        public string Text { get; set; } = string.Empty;

        /// <summary>Display-ready text (emoji symbolic form + simple format markers / HTML-safe).</summary>
        [XmlElement("formatted_text")]
        public string FormattedText { get; set; } = string.Empty;

        [XmlElement("is_outgoing")]
        public bool IsOutgoing { get; set; }

        [XmlElement("date")]
        public DateTime Date { get; set; }

        [XmlElement("reply_to_msg_id")]
        public int ReplyToMsgId { get; set; }

        /// <summary>Display name of the author of the quoted / replied-to message.</summary>
        [XmlElement("reply_from_name")]
        public string ReplyFromName { get; set; } = string.Empty;

        [XmlElement("reply_preview")]
        public string ReplyPreview { get; set; } = string.Empty;

        [XmlElement("is_mention")]
        public bool IsMention { get; set; }

        [XmlElement("media_kind")]
        public string MediaKind { get; set; } = TelegramMediaKinds.None;

        [XmlElement("media_label")]
        public string MediaLabel { get; set; } = string.Empty;

        [XmlElement("media_file_name")]
        public string MediaFileName { get; set; } = string.Empty;

        [XmlElement("has_media")]
        public bool HasMedia { get; set; }

        [XmlArray("buttons")]
        [XmlArrayItem("button")]
        public List<TelegramBotButtonModel> Buttons { get; set; } = new List<TelegramBotButtonModel>();

        /// <summary>Sum of all reaction counts on this message.</summary>
        [XmlElement("reaction_count")]
        public int ReactionCount { get; set; }

        /// <summary>True when this account already chose a reaction (e.g. 👍).</summary>
        [XmlElement("has_my_reaction")]
        public bool HasMyReaction { get; set; }
    }

    /// <summary>
    /// Opaque reference used to re-download media on demand (no auto-download).
    /// </summary>
    public class TelegramMediaRef
    {
        [XmlElement("account_id")]
        public string AccountId { get; set; } = string.Empty;

        [XmlElement("chat_key")]
        public string ChatKey { get; set; } = string.Empty;

        [XmlElement("message_id")]
        public int MessageId { get; set; }

        [XmlElement("media_kind")]
        public string MediaKind { get; set; } = TelegramMediaKinds.None;

        [XmlElement("file_name")]
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Bot reply-keyboard or inline button.
    /// </summary>
    public class TelegramBotButtonModel
    {
        [XmlAttribute("text")]
        public string Text { get; set; } = string.Empty;

        /// <summary>Callback data (UTF-8) for inline callback buttons.</summary>
        [XmlElement("data")]
        public string Data { get; set; } = string.Empty;

        [XmlElement("url")]
        public string Url { get; set; } = string.Empty;

        [XmlElement("is_web_app")]
        public bool IsWebApp { get; set; }

        [XmlElement("message_id")]
        public int MessageId { get; set; }
    }

    /// <summary>
    /// Result of a Telegram client operation.
    /// </summary>
    public class TelegramOperationResult
    {
        public bool Success { get; set; }

        public string ErrorMessage { get; set; }

        public TelegramAuthState AuthState { get; set; } = TelegramAuthState.LoggedOut;

        public string Url { get; set; }

        public string LocalPath { get; set; }

        /// <summary>Chat key opened by a deep-link operation (e.g. <c>channel:123</c>).</summary>
        public string ChatKey { get; set; }

        /// <summary>Human-readable hint about where Telegram sent the login code.</summary>
        public string CodeDeliveryHint { get; set; }

        public static TelegramOperationResult Ok(TelegramAuthState state = TelegramAuthState.Ready, string codeDeliveryHint = null)
        {
            return new TelegramOperationResult { Success = true, AuthState = state, CodeDeliveryHint = codeDeliveryHint };
        }

        public static TelegramOperationResult Fail(string message, TelegramAuthState state = TelegramAuthState.Error, string codeDeliveryHint = null)
        {
            return new TelegramOperationResult
            {
                Success = false,
                ErrorMessage = message,
                AuthState = state,
                CodeDeliveryHint = codeDeliveryHint,
            };
        }
    }

    /// <summary>
    /// Authentication / connection state for an account (or the pending login flow).
    /// </summary>
    public enum TelegramAuthState
    {
        LoggedOut = 0,
        NeedApiCredentials = 1,
        NeedPhone = 2,
        NeedCode = 3,
        NeedPassword = 4,
        Ready = 5,
        Connecting = 6,
        Error = 7,
        /// <summary>Waiting for the user to scan a login QR with the official Telegram app.</summary>
        NeedQr = 8,
    }

    /// <summary>
    /// Media kind constants persisted as strings for XML friendliness.
    /// </summary>
    public static class TelegramMediaKinds
    {
        public const string None = "None";
        public const string Photo = "Photo";
        public const string Video = "Video";
        public const string Gif = "Gif";
        public const string Audio = "Audio";
        public const string Voice = "Voice";
        public const string Document = "Document";
        public const string Sticker = "Sticker";
        public const string Story = "Story";
        public const string Other = "Other";
    }

    /// <summary>
    /// Peer type constants for <see cref="TelegramChatModel.PeerType"/>.
    /// </summary>
    public static class TelegramPeerTypes
    {
        public const string User = "user";
        public const string Chat = "chat";
        public const string Channel = "channel";
    }

    /// <summary>
    /// Formatting flags for outgoing text (combined with markdown-lite parsing).
    /// </summary>
    [Flags]
    public enum TelegramTextFormat
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Code = 4,
        Pre = 8,
        Underline = 16,
        Strike = 32,
        Spoiler = 64,
        Link = 128,
        MarkdownLite = 256,
    }

    /// <summary>
    /// A text span produced by markdown-lite parsing (mapped to TL entities when sending).
    /// </summary>
    public class TelegramTextEntitySpan
    {
        public int Offset { get; set; }

        public int Length { get; set; }

        public TelegramTextFormat Kind { get; set; }

        public string Url { get; set; }

        public string Language { get; set; }
    }
}
