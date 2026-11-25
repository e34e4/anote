// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Identifies internal aNote tags that should not appear in the user-facing tag filter tree.
    /// Filtering by these tags (e.g. <c>__anote_widget__</c>) would hide almost all notes.
    /// </summary>
    public static class NoteSystemTags
    {
        /// <summary>Prefix shared by all internal capture / hub note tags.</summary>
        public const string Prefix = "__anote_";

        /// <summary>Returns true when the tag is reserved for internal aNote notes.</summary>
        public static bool IsSystemTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            return tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Removes system tags from a filter list (returns a new list).</summary>
        public static List<string> WithoutSystemTags(IEnumerable<string> tags)
        {
            if (tags == null)
                return new List<string>();
            return tags.Where(tag => !IsSystemTag(tag)).ToList();
        }
    }
}
