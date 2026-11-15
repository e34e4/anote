// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// The open tabs of the Abro browser. This is the content of the local <c>tabs.json</c> and
    /// of the optional tab sync file on WebDAV.
    /// </summary>
    public class AbroTabsStateModel
    {
        /// <summary>Gets or sets the index of the selected tab.</summary>
        public int Selected { get; set; }

        /// <summary>Gets or sets the urls of the open tabs.</summary>
        public List<string> Tabs { get; set; } = new List<string>();
    }
}
