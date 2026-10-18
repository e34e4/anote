// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Carries a request (e.g. from the floating button menu) to activate a specific tab in
    /// <see cref="Views.NoteRepositoryPage"/>.
    /// </summary>
    public class TabIntentRequest
    {
        /// <summary>
        /// Gets or sets the pending request which should be handled by the main page.
        /// </summary>
        public static TabIntentRequest Pending { get; set; }

        /// <summary>
        /// Gets or sets the zero-based tab index to activate (0..12).
        /// </summary>
        public int TabIndex { get; set; }
    }
}
