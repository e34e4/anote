// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Holds a pending dial/phone intent request when aNote is opened via a tel: link or dial intent.
    /// </summary>
    public class PhoneIntentRequest
    {
        /// <summary>Gets or sets the pending request to be consumed by the main page.</summary>
        public static PhoneIntentRequest Pending { get; set; }

        /// <summary>Gets or sets the phone number to pre-fill in the dialer numpad.</summary>
        public string PhoneNumber { get; set; }
    }
}
