// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// A service to store and manage the history of scanned QR codes on the local device.
    /// </summary>
    public interface IQrHistoryService
    {
        /// <summary>
        /// Loads the QR history, or returns an empty model when no history exists yet.
        /// </summary>
        /// <returns>The QR history.</returns>
        QrHistoryModel LoadHistoryOrDefault();

        /// <summary>
        /// Adds a scanned text to the history. An identical existing item is moved to the top
        /// instead of being duplicated.
        /// </summary>
        /// <param name="text">The decoded text of the QR code.</param>
        /// <param name="maxItems">Maximum number of items to keep.</param>
        /// <returns>The added or promoted item, or null if nothing was added.</returns>
        QrItemModel AddText(string text, int maxItems);

        /// <summary>
        /// Deletes an item from the history.
        /// </summary>
        /// <param name="id">Id of the item to delete.</param>
        void Delete(Guid id);

        /// <summary>
        /// Removes all items from the history.
        /// </summary>
        void ClearAll();
    }
}
