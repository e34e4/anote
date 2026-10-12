// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// A single text message of the device message store.
    /// </summary>
    public class SmsMessageModel
    {
        /// <summary>Gets or sets the id of the message in the device message store.</summary>
        public long Id { get; set; }

        /// <summary>Gets or sets the id of the thread the message belongs to.</summary>
        public long ThreadId { get; set; }

        /// <summary>Gets or sets the phone number of the sender or receiver.</summary>
        public string Address { get; set; }

        /// <summary>Gets or sets the text of the message.</summary>
        public string Body { get; set; }

        /// <summary>Gets or sets the time of the message in UTC.</summary>
        public DateTime DateUtc { get; set; }

        /// <summary>Gets or sets a value indicating whether the message was received.</summary>
        public bool Incoming { get; set; }
    }
}
