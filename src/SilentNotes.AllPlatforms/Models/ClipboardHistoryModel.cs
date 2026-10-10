// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of the captured clipboard history. Items are stored newest first.
    /// </summary>
    [XmlRoot(ElementName = "anote_clipboard")]
    public class ClipboardHistoryModel
    {
#if (RELEASE)
        public const string HistoryFileName = "anote_clipboard_history.config";
#else
        public const string HistoryFileName = "anote_clipboard_history_dev.config";
#endif

        private List<ClipboardItemModel> _items;

        /// <summary>
        /// Gets or sets the list of captured clipboard items, the newest item is the first one.
        /// </summary>
        [XmlArray("items")]
        [XmlArrayItem("item")]
        public List<ClipboardItemModel> Items
        {
            get { return _items ?? (_items = new List<ClipboardItemModel>()); }
            set { _items = value; }
        }
    }
}
