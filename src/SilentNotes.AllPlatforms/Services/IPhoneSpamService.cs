// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Service for managing the phone number blocklist (with wildcard masks like <c>+7903*</c>),
    /// fetching/updating the offline spam database (AGContacts / <c>callerid.mbagc.ru</c> format
    /// and Russian community sources), and performing Russian AON/MNP caller lookups.
    /// </summary>
    public interface IPhoneSpamService
    {
        /// <summary>Gets or sets a value indicating whether incoming calls matching blocklist masks are automatically rejected.</summary>
        bool IsBlocklistEnabled { get; set; }

        /// <summary>Gets or sets a value indicating whether incoming calls found as negative/spam in the spam DB are automatically rejected.</summary>
        bool AutoBlockSpam { get; set; }

        /// <summary>Gets or sets a value indicating whether contacts from the repository are exempt from automatic blocking.</summary>
        bool SkipContactsWhenBlocking { get; set; }

        /// <summary>Gets or sets a custom spam list URL (TXT/CSV/XML) to include when updating the spam database.</summary>
        string CustomSpamSourceUrl { get; set; }

        /// <summary>Gets all configured blocklist rules (newest first).</summary>
        IReadOnlyList<PhoneBlockRuleModel> GetBlockRules();

        /// <summary>Adds or updates a blocklist rule with support for wildcard masks such as <c>+7903*</c>.</summary>
        PhoneBlockRuleModel AddBlockRule(string pattern, string label = null);

        /// <summary>Enables or disables an existing blocklist rule.</summary>
        void SetBlockRuleEnabled(Guid id, bool isEnabled);

        /// <summary>Removes a blocklist rule by its identifier.</summary>
        void DeleteBlockRule(Guid id);

        /// <summary>Finds the first enabled blocklist rule matching <paramref name="phoneNumber"/>, or null if none.</summary>
        PhoneBlockRuleModel FindMatchingBlockRule(string phoneNumber);

        /// <summary>Gets metadata and statistics about the local spam database.</summary>
        PhoneSpamDatabaseStatus GetDatabaseStatus();

        /// <summary>Searches locally stored spam/caller-ID entries.</summary>
        IReadOnlyList<PhoneSpamEntryModel> SearchLocalSpamEntries(string query = null, int maxResults = 150);

        /// <summary>Looks up a phone number in the local offline spam database without network calls.</summary>
        PhoneSpamEntryModel LookupLocal(string phoneNumber);

        /// <summary>
        /// Looks up a phone number using both the local database and Russian online AON/MNP services
        /// (<c>callerid.mbagc.ru</c>, <c>zvonili.me</c>, <c>whocalls.ru</c>, <c>voprosoff.net</c>,
        /// <c>zvonili.com</c>, <c>callfilter.app</c>, <c>num.voxlink.ru</c>, <c>htmlweb.ru</c>).
        /// </summary>
        Task<PhoneSpamEntryModel> LookupOnlineAsync(string phoneNumber);

        /// <summary>
        /// Downloads and updates the local spam database from AGContacts (<c>callerid.mbagc.ru</c>
        /// LZMA protobuf shards &amp; accumulative deltas), Russian AON feeds (<c>zvonili.me</c>,
        /// <c>whocalls.ru</c>), Russian community spam lists, and any configured custom URL.
        /// </summary>
        Task<(int totalRecords, int updatedRecords, string statusText)> UpdateSpamDatabaseAsync(IProgress<string> progress = null);

        /// <summary>Imports spam entries from a TXT, CSV, XML, or JSON string.</summary>
        int ImportSpamData(string content, string sourceName = "import");

        /// <summary>Adds or updates a single entry in the local spam database.</summary>
        void UpsertSpamEntry(PhoneSpamEntryModel entry);

        /// <summary>Removes a single entry from the local spam database.</summary>
        void RemoveSpamEntry(string phoneNumber);

        /// <summary>Clears all entries from the local spam database.</summary>
        void ClearSpamDatabase();

        /// <summary>
        /// Evaluates an incoming call against the blocklist and spam database, updating hit statistics
        /// if a rule matched.
        /// </summary>
        IncomingCallDecision EvaluateIncomingCall(string phoneNumber, bool isKnownContact);
    }
}
