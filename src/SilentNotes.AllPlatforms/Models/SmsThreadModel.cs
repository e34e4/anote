// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// A conversation (thread) of the device message store.
    /// </summary>
    public class SmsThreadModel
    {
        /// <summary>Gets or sets the id of the thread in the device message store.</summary>
        public long ThreadId { get; set; }

        /// <summary>Gets or sets the phone number of the conversation partner.</summary>
        public string Address { get; set; }

        /// <summary>Gets or sets a friendly name for the address, if one is known.</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the text of the last message.</summary>
        public string Snippet { get; set; }

        /// <summary>Gets or sets the time of the last message in UTC.</summary>
        public DateTime DateUtc { get; set; }

        /// <summary>Gets or sets the number of unread messages in the thread.</summary>
        public int UnreadCount { get; set; }

        /// <summary>Gets the name to show for the conversation.</summary>
        public string Title
        {
            get { return string.IsNullOrWhiteSpace(DisplayName) ? Address : DisplayName; }
        }
    }
}
