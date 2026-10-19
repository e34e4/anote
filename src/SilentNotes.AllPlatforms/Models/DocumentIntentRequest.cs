// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Holds a pending document VIEW/SEND intent so the Reader tab can open it.
    /// </summary>
    public class DocumentIntentRequest
    {
        /// <summary>Gets or sets a pending request waiting to be consumed by the UI.</summary>
        public static DocumentIntentRequest Pending { get; set; }

        /// <summary>Gets or sets the content URI or filesystem path.</summary>
        public string UriOrPath { get; set; }

        /// <summary>Gets or sets the MIME type when known.</summary>
        public string MimeType { get; set; }

        /// <summary>Gets or sets a display name hint.</summary>
        public string DisplayName { get; set; }
    }
}
