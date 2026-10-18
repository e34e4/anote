// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// Rating classification for a caller ID / spam database entry (matching AGContacts / Чей номер+).
    /// </summary>
    public enum PhoneSpamRating
    {
        /// <summary>Unknown or neutral rating.</summary>
        Neutral = 0,

        /// <summary>Positive / safe caller rating.</summary>
        Positive = 1,

        /// <summary>Negative / unwanted / spam caller rating.</summary>
        Negative = 2,
    }

    /// <summary>
    /// Represents a single phone number entry in the spam / caller-ID database.
    /// </summary>
    public class PhoneSpamEntryModel
    {
        /// <summary>Gets or sets the canonical digits of the phone number (e.g. "79036189086").</summary>
        public string Number { get; set; } = string.Empty;

        /// <summary>Gets or sets the overall rating (Negative = spam/scam, Positive = safe, Neutral = info).</summary>
        public PhoneSpamRating Rating { get; set; } = PhoneSpamRating.Negative;

        /// <summary>Gets or sets the number of positive user reviews.</summary>
        public int PositiveCount { get; set; }

        /// <summary>Gets or sets the number of negative user reviews.</summary>
        public int NegativeCount { get; set; }

        /// <summary>Gets or sets the number of neutral user reviews.</summary>
        public int NeutralCount { get; set; }

        /// <summary>Gets or sets the caller category (e.g. "Мошенничество", "Реклама", "Коллекторы", "Опрос").</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Gets or sets the caller title or organization name.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Gets or sets the review summary or comment.</summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>Gets or sets the telecom operator (MNP/plan).</summary>
        public string Operator { get; set; } = string.Empty;

        /// <summary>Gets or sets the geographical region.</summary>
        public string Region { get; set; } = string.Empty;

        /// <summary>Gets or sets the source service name (e.g. "callerid.mbagc.ru", "zvonili.me", "whocalls.ru", "voprosoff.net").</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Gets or sets when this record was updated (UTC).</summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Gets a value indicating whether this entry indicates an unwanted/spam caller.</summary>
        public bool IsSpam => Rating == PhoneSpamRating.Negative || NegativeCount > PositiveCount;

        /// <summary>Gets a concise single-line summary for display in call logs, numpad, and notifications.</summary>
        public string SummaryText
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Category))
                    parts.Add(Category);
                if (!string.IsNullOrWhiteSpace(Title) && !string.Equals(Title, Category, StringComparison.OrdinalIgnoreCase))
                    parts.Add(Title);
                if (!string.IsNullOrWhiteSpace(Comment) && parts.Count < 2)
                    parts.Add(Comment);
                if (!string.IsNullOrWhiteSpace(Operator))
                    parts.Add(Operator);
                if (!string.IsNullOrWhiteSpace(Region) && Region != "-")
                    parts.Add(Region);
                if (NegativeCount > 0 || PositiveCount > 0)
                    parts.Add($"-{NegativeCount}/+{PositiveCount}");
                if (!string.IsNullOrWhiteSpace(Source))
                    parts.Add($"[{Source}]");
                return parts.Count > 0 ? string.Join(" · ", parts) : Source;
            }
        }
    }

    /// <summary>
    /// Metadata and statistics about the local spam database and configured Russian AON/MNP sources.
    /// </summary>
    public class PhoneSpamDatabaseStatus
    {
        /// <summary>Gets or sets the AGContacts / mbagc.ru stable DB version date (e.g. "2025-07-09").</summary>
        public string StableVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the AGContacts / mbagc.ru accumulative version timestamp (e.g. "2026-10-18-14-43").</summary>
        public string AccumulativeVersion { get; set; } = string.Empty;

        /// <summary>Gets or sets the total number of records reported by the remote AGContacts server.</summary>
        public int RemoteTotalRecords { get; set; }

        /// <summary>Gets or sets the number of locally indexed spam/caller-ID records.</summary>
        public int LocalRecordCount { get; set; }

        /// <summary>Gets or sets the number of locally cached AGContacts prefix shards.</summary>
        public int CachedShardCount { get; set; }

        /// <summary>Gets or sets the UTC time of the last database update.</summary>
        public DateTime? LastUpdatedUtc { get; set; }
    }

    /// <summary>
    /// Result of evaluating an incoming call against the blocklist and spam database.
    /// </summary>
    public class IncomingCallDecision
    {
        /// <summary>Gets or sets a value indicating whether the incoming call should be rejected automatically.</summary>
        public bool ShouldBlock { get; set; }

        /// <summary>Gets or sets the reason the call was blocked (e.g. matched mask or spam category).</summary>
        public string BlockReason { get; set; }

        /// <summary>Gets or sets the matched blocklist rule, if any.</summary>
        public PhoneBlockRuleModel MatchedRule { get; set; }

        /// <summary>Gets or sets the matched spam/caller-ID entry, if any.</summary>
        public PhoneSpamEntryModel SpamEntry { get; set; }
    }
}
