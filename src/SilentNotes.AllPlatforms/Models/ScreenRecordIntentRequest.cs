// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Pending request from the floating quick menu to toggle screen recording on the Rec tab.
    /// </summary>
    public class ScreenRecordIntentRequest
    {
        /// <summary>Gets or sets the pending request, or null.</summary>
        public static ScreenRecordIntentRequest Pending { get; set; }

        /// <summary>When true, stop an active recording; otherwise start one.</summary>
        public bool Stop { get; set; }
    }
}
