// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Sort order for File, Player, and Reader file browsers (folders always listed first).
    /// </summary>
    public enum FileBrowserSortMode
    {
        NameAsc,
        DateDesc,
        SizeDesc,
    }
}