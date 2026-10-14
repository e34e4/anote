// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of the QR tab, it stores the history of scanned and decoded QR codes.
    /// </summary>
    [XmlRoot(ElementName = "anote_qr")]
    public class QrHistoryModel
    {
        /// <summary>
        /// Gets or sets the maximum number of items which are kept in the history.
        /// </summary>
        public const int MaxHistoryItems = 200;

        public const string FileName = "anote_qr.config";

        private List<QrItemModel> _items;

        /// <summary>Gets or sets the history items, the newest item is at the top.</summary>
        [XmlArray("items")]
        [XmlArrayItem("item")]
        public List<QrItemModel> Items
        {
            get { return _items ?? (_items = new List<QrItemModel>()); }
            set { _items = value; }
        }
    }

    /// <summary>
    /// A single scanned or decoded QR code of the history.
    /// </summary>
    public class QrItemModel
    {
        /// <summary>Gets or sets the id of the item.</summary>
        [XmlAttribute(AttributeName = "id")]
        public Guid Id { get; set; }

        /// <summary>Gets or sets the local time when the code was scanned.</summary>
        [XmlAttribute(AttributeName = "created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>Gets or sets the decoded text of the QR code.</summary>
        [XmlElement(ElementName = "text")]
        public string Text { get; set; }
    }
}
