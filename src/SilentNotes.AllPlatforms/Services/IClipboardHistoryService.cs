// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// A service to store and manage the captured clipboard history on the local device.
    /// </summary>
    public interface IClipboardHistoryService
    {
        /// <summary>
        /// Loads the clipboard history, or returns an empty model when no history exists yet.
        /// </summary>
        /// <returns>The clipboard history.</returns>
        ClipboardHistoryModel LoadHistoryOrDefault();

        /// <summary>
        /// Tries to save the clipboard history to the local device.
        /// </summary>
        /// <param name="model">The history to store.</param>
        /// <returns>True if the history could be stored, otherwise false.</returns>
        bool TrySaveHistory(ClipboardHistoryModel model);

        /// <summary>
        /// Adds a copied text to the history. An identical existing item is moved to the top
        /// instead of being duplicated.
        /// </summary>
        /// <param name="text">The copied text.</param>
        /// <param name="maxNonFavoriteItems">Maximum number of non-favorite items to keep.</param>
        /// <returns>The added or promoted item, or null if nothing was added.</returns>
        ClipboardItemModel AddText(string text, int maxNonFavoriteItems);

        /// <summary>
        /// Adds a copied image to the history. An identical existing item is moved to the top
        /// instead of being duplicated.
        /// </summary>
        /// <param name="fullImage">The encoded full size image (PNG).</param>
        /// <param name="thumbnail">The encoded thumbnail image (PNG), may be null.</param>
        /// <param name="maxNonFavoriteItems">Maximum number of non-favorite items to keep.</param>
        /// <returns>The added or promoted item, or null if nothing was added.</returns>
        ClipboardItemModel AddImage(byte[] fullImage, byte[] thumbnail, int maxNonFavoriteItems);

        /// <summary>
        /// Toggles the favorite state of an item.
        /// </summary>
        /// <param name="id">Id of the item to modify.</param>
        void ToggleFavorite(Guid id);

        /// <summary>
        /// Deletes an item from the history.
        /// </summary>
        /// <param name="id">Id of the item to delete.</param>
        void Delete(Guid id);

        /// <summary>
        /// Removes all non-favorite items from the history.
        /// </summary>
        void ClearNonFavorites();

        /// <summary>
        /// Removes all items from the history.
        /// </summary>
        void ClearAll();

        /// <summary>
        /// Gets the absolute path of the full size image of an item.
        /// </summary>
        /// <param name="fileName">The file name stored in <see cref="ClipboardItemModel.ImageFileName"/>.</param>
        /// <returns>Absolute file path, or null.</returns>
        string GetFullImagePath(string fileName);

        /// <summary>
        /// Gets the absolute path of the thumbnail image of an item.
        /// </summary>
        /// <param name="fileName">The file name stored in <see cref="ClipboardItemModel.ImageFileName"/>.</param>
        /// <returns>Absolute file path, or null.</returns>
        string GetThumbImagePath(string fileName);
    }
}
