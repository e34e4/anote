// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Carries a global search result to the main page: the tab which should be opened and the
    /// filter text to apply there.
    /// </summary>
    public class SearchIntentRequest
    {
        /// <summary>
        /// Gets or sets the pending request which should be handled by the main page.
        /// </summary>
        public static SearchIntentRequest Pending { get; set; }

        /// <summary>
        /// Gets or sets the stable tab index to activate (see <see cref="MainViewTabCatalog"/>).
        /// </summary>
        public int TabIndex { get; set; }

        /// <summary>
        /// Gets or sets the filter text to apply to the tab.
        /// </summary>
        public string Filter { get; set; }
    }
}
