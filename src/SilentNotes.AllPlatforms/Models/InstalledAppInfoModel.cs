// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Represents an launchable application or action that can be assigned to a floating button gesture.
    /// </summary>
    public class InstalledAppInfoModel
    {
        /// <summary>Gets or sets the package name or built-in action identifier.</summary>
        public string PackageName { get; set; }

        /// <summary>Gets or sets the human-readable application label.</summary>
        public string Label { get; set; }
    }
}
