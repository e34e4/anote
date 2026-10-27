// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Holds a pending SMS compose request when aNote is opened via an sms/smsto link, or when a
    /// contact's SMS action is used inside the app.
    /// </summary>
    public class SmsIntentRequest
    {
        /// <summary>Gets or sets the pending request to be consumed by the main page.</summary>
        public static SmsIntentRequest Pending { get; set; }

        /// <summary>Gets or sets the phone number to pre-fill in the new SMS composer.</summary>
        public string PhoneNumber { get; set; }

        /// <summary>Gets or sets an optional message body to pre-fill in the composer.</summary>
        public string Body { get; set; }
    }
}
