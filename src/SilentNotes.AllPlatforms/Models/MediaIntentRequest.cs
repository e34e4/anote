// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Holds a pending media VIEW/SEND intent so the Player tab can open and play it.
    /// </summary>
    public class MediaIntentRequest
    {
        /// <summary>Gets or sets the pending request to be consumed by the main page.</summary>
        public static MediaIntentRequest Pending { get; set; }

        /// <summary>Gets or sets a content:// or file:// URI, or an absolute filesystem path.</summary>
        public string UriOrPath { get; set; }

        /// <summary>Gets or sets the MIME type from the intent, when available.</summary>
        public string MimeType { get; set; }

        /// <summary>Gets or sets the display name when known.</summary>
        public string DisplayName { get; set; }
    }
}
