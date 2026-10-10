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
    /// Serializeable model of a single clipboard item as it travels through the synchronized
    /// repository. Unlike <see cref="ClipboardItemModel"/>, which only references an image file on
    /// disk, this model carries the encoded image data, so it can be uploaded to the cloud.
    /// </summary>
    /// <remarks>
    /// This is a transient transfer representation: it is populated just before a repository is
    /// encrypted for upload, and consumed again when a downloaded repository is merged back into
    /// the local clipboard history. It is never persisted in the local repository file.
    /// </remarks>
    public class ClipboardTransferItemModel
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
        /// Gets or sets the time in UTC, when the item was last modified (used for merging).
        /// </summary>
        [XmlAttribute("modified_utc")]
        public DateTime ModifiedUtc { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the item is marked as favorite.
        /// </summary>
        [XmlAttribute("favorite")]
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the item was deleted. Deleted items are kept as
        /// tombstones, so a deletion can be synchronized without resurrecting the item.
        /// </summary>
        [XmlAttribute("deleted")]
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Gets or sets the kind of the captured content.
        /// </summary>
        [XmlAttribute("kind")]
        public ClipboardItemKind Kind { get; set; }

        /// <summary>
        /// Gets or sets a hash of the content, used to detect duplicated clips.
        /// </summary>
        [XmlAttribute("hash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// Gets or sets the copied text, only used for <see cref="ClipboardItemKind.Text"/> items.
        /// </summary>
        [XmlElement("text")]
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets the full image as base64 string, only used for
        /// <see cref="ClipboardItemKind.Image"/> items.
        /// </summary>
        [XmlElement("image_data")]
        public string ImageData { get; set; }

        /// <summary>
        /// Gets or sets the thumbnail image as base64 string, only used for
        /// <see cref="ClipboardItemKind.Image"/> items.
        /// </summary>
        [XmlElement("thumb_data")]
        public string ThumbData { get; set; }

        /// <summary>
        /// Makes a deep copy of the item.
        /// </summary>
        /// <returns>Copy of the item.</returns>
        public ClipboardTransferItemModel Clone()
        {
            return (ClipboardTransferItemModel)MemberwiseClone();
        }
    }

    /// <summary>
    /// List of <see cref="ClipboardTransferItemModel"/> objects, used by a
    /// <see cref="NoteRepositoryModel"/>.
    /// </summary>
    public class ClipboardTransferListModel : List<ClipboardTransferItemModel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardTransferListModel"/> class.
        /// </summary>
        public ClipboardTransferListModel()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardTransferListModel"/> class.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new list.</param>
        public ClipboardTransferListModel(IEnumerable<ClipboardTransferItemModel> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Searches for an item with a given id in the list and returns the found item.
        /// </summary>
        /// <param name="id">Search for the item with this id.</param>
        /// <returns>Found item, or null if no such item exists.</returns>
        public ClipboardTransferItemModel FindById(Guid id)
        {
            return Find(item => item.Id == id);
        }
    }
}
