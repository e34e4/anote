// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Gives the sms tab access to the messages of the operating system. On Android this reads the
    /// system message store and sends messages with the SmsManager. Being the default SMS app is
    /// required to receive and store incoming messages.
    /// </summary>
    public interface IDeviceSmsService
    {
        /// <summary>Gets a value indicating whether the platform supports messages.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether the app may read the messages.</summary>
        bool HasReadPermission { get; }

        /// <summary>Gets a value indicating whether the app may send messages.</summary>
        bool HasSendPermission { get; }

        /// <summary>Asks the user for the required message permissions.</summary>
        /// <returns>True if the permissions were granted, otherwise false.</returns>
        Task<bool> RequestPermissionsAsync();

        /// <summary>Gets a value indicating whether aNote is the default SMS app.</summary>
        bool IsDefaultSmsApp { get; }

        /// <summary>Asks the user to make aNote the default SMS app.</summary>
        /// <returns>True if aNote became the default SMS app, otherwise false.</returns>
        Task<bool> RequestDefaultSmsAppAsync();

        /// <summary>Gets the list of conversations, the newest first.</summary>
        Task<IReadOnlyList<SmsThreadModel>> GetThreadsAsync();

        /// <summary>Gets the messages of a conversation, the oldest first.</summary>
        /// <param name="threadId">The id of the thread.</param>
        Task<IReadOnlyList<SmsMessageModel>> GetMessagesAsync(long threadId);

        /// <summary>Sends a text message.</summary>
        /// <param name="address">The phone number to send to.</param>
        /// <param name="body">The text of the message.</param>
        Task SendSmsAsync(string address, string body);

        /// <summary>Marks all messages of a conversation as read.</summary>
        /// <param name="threadId">The id of the thread.</param>
        Task MarkThreadReadAsync(long threadId);
    }
}
