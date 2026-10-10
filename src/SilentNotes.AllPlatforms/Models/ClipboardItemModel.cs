// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of a single captured clipboard item, either a text or an image.
    /// </summary>
    public class ClipboardItemModel
    {
        /// <summary>
        /// Gets or sets the unique id of the item.
        /// </summary>
        [XmlAttribute("id")]
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the time in UTC, when the clip was captured.
        /// </summary>
        [XmlAttribute("created_utc")]
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the item is marked as favorite. Favorite items
        /// are never removed by the history size limit.
        /// </summary>
        [XmlAttribute("favorite")]
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Gets or sets the kind of the captured content.
        /// </summary>
        [XmlAttribute("kind")]
        public ClipboardItemKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the copied text, only used for <see cref="ClipboardItemKind.Text"/> items.
        /// </summary>
        [XmlElement("text")]
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets the file name (without directory) of the full image, only used for
        /// <see cref="ClipboardItemKind.Image"/> items.
        /// </summary>
        [XmlElement("image_name")]
        public string ImageFileName { get; set; }

        /// <summary>
        /// Gets or sets a hash of the content, used to detect duplicated clips.
        /// </summary>
        [XmlAttribute("hash")]
        public string ContentHash { get; set; }
    }

    /// <summary>
    /// Enumeration of the possible kinds of a captured clipboard item.
    /// </summary>
    public enum ClipboardItemKind
    {
        /// <summary>A copied plain text.</summary>
        Text,

        /// <summary>A copied image.</summary>
        Image,
    }
}
