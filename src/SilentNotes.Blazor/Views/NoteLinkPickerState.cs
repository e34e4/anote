// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Views
{
    /// <summary>
    /// A single entry in the "insert link" picker of the note editor.
    /// </summary>
    internal class NoteLinkPickerEntry
    {
        /// <summary>Gets or sets the display title.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the kind of the target (Note, Contact or Bookmark).</summary>
        public string Kind { get; set; }

        /// <summary>Gets or sets the href which is inserted into the note.</summary>
        public string Url { get; set; }

        /// <summary>Gets or sets a secondary description.</summary>
        public string Detail { get; set; }

        /// <summary>Gets or sets the text which is searched by the filter.</summary>
        public string SearchText { get; set; }
    }

    /// <summary>
    /// State of the "insert link" picker of the note editor.
    /// </summary>
    internal class NoteLinkPickerState
    {
        /// <summary>Gets or sets a value indicating whether the picker is visible.</summary>
        public bool IsOpen { get; set; }

        /// <summary>Gets or sets the current search filter.</summary>
        public string Filter { get; set; } = string.Empty;
    }
}
