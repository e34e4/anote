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
    /// Serializable mail store containing accounts and cached/local messages.
    /// </summary>
    [XmlRoot("anote_mail_store")]
    public class MailStoreModel
    {
        [XmlArray("accounts")]
        [XmlArrayItem("account")]
        public List<MailAccountModel> Accounts { get; set; } = new List<MailAccountModel>();

        [XmlArray("messages")]
        [XmlArrayItem("message")]
        public List<MailMessageModel> Messages { get; set; } = new List<MailMessageModel>();
    }

    /// <summary>
    /// Represents an email account (IMAP + SMTP) configured in the Mail tab.
    /// </summary>
    public class MailAccountModel
    {
        public MailAccountModel()
        {
            Id = Guid.NewGuid().ToString("N");
            ImapPort = 993;
            ImapUseSsl = true;
            SmtpPort = 465;
            SmtpUseSsl = true;
        }

        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlElement("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [XmlElement("email")]
        public string EmailAddress { get; set; } = string.Empty;

        [XmlElement("imap_host")]
        public string ImapHost { get; set; } = string.Empty;

        [XmlElement("imap_port")]
        public int ImapPort { get; set; }

        [XmlElement("imap_ssl")]
        public bool ImapUseSsl { get; set; }

        [XmlElement("smtp_host")]
        public string SmtpHost { get; set; } = string.Empty;

        [XmlElement("smtp_port")]
        public int SmtpPort { get; set; }

        [XmlElement("smtp_ssl")]
        public bool SmtpUseSsl { get; set; }

        [XmlElement("username")]
        public string Username { get; set; } = string.Empty;

        [XmlIgnore]
        public string Password { get; set; } = string.Empty;

        [XmlElement("encrypted_password")]
        public string EncryptedPassword { get; set; }
    }

    /// <summary>
    /// Represents a single email message in Inbox, Starred, Sent, Drafts, or Trash.
    /// </summary>
    public class MailMessageModel
    {
        public MailMessageModel()
        {
            Id = Guid.NewGuid().ToString("N");
            Folder = MailFolders.Inbox;
            DateUtc = DateTime.UtcNow;
        }

        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlElement("account_id")]
        public string AccountId { get; set; } = string.Empty;

        [XmlElement("remote_uid")]
        public string RemoteUid { get; set; } = string.Empty;

        [XmlElement("message_id")]
        public string MessageId { get; set; } = string.Empty;

        [XmlElement("folder")]
        public string Folder { get; set; }

        [XmlElement("from")]
        public string From { get; set; } = string.Empty;

        [XmlElement("to")]
        public string To { get; set; } = string.Empty;

        [XmlElement("cc")]
        public string Cc { get; set; } = string.Empty;

        [XmlElement("bcc")]
        public string Bcc { get; set; } = string.Empty;

        [XmlElement("subject")]
        public string Subject { get; set; } = string.Empty;

        [XmlElement("snippet")]
        public string Snippet { get; set; } = string.Empty;

        [XmlElement("body_text")]
        public string BodyText { get; set; } = string.Empty;

        [XmlElement("body_html")]
        public string BodyHtml { get; set; } = string.Empty;

        [XmlElement("date_utc")]
        public DateTime DateUtc { get; set; }

        [XmlElement("is_read")]
        public bool IsRead { get; set; }

        [XmlElement("is_starred")]
        public bool IsStarred { get; set; }

        [XmlArray("attachments")]
        [XmlArrayItem("attachment")]
        public List<MailAttachmentMetaModel> Attachments { get; set; } = new List<MailAttachmentMetaModel>();
    }

    /// <summary>
    /// Metadata for an email attachment.
    /// </summary>
    public class MailAttachmentMetaModel
    {
        [XmlAttribute("name")]
        public string FileName { get; set; } = string.Empty;

        [XmlAttribute("mime")]
        public string ContentType { get; set; } = "application/octet-stream";

        [XmlAttribute("size")]
        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Standard folder constants for the Mail tab.
    /// </summary>
    public static class MailFolders
    {
        public const string Inbox = "Inbox";
        public const string Starred = "Starred";
        public const string Sent = "Sent";
        public const string Drafts = "Drafts";
        public const string Trash = "Trash";
        public const string All = "All";
    }
}
