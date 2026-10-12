// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IDeviceSmsService"/> interface for the Windows platform.
    /// Windows does not provide a message store which could be used here.
    /// </summary>
    internal class DeviceSmsService : IDeviceSmsService
    {
        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public bool HasReadPermission
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public bool HasSendPermission
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public Task<bool> RequestPermissionsAsync()
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public bool IsDefaultSmsApp
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public Task<bool> RequestDefaultSmsAppAsync()
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SmsThreadModel>> GetThreadsAsync()
        {
            return Task.FromResult<IReadOnlyList<SmsThreadModel>>(new List<SmsThreadModel>());
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SmsMessageModel>> GetMessagesAsync(long threadId)
        {
            return Task.FromResult<IReadOnlyList<SmsMessageModel>>(new List<SmsMessageModel>());
        }

        /// <inheritdoc/>
        public Task SendSmsAsync(string address, string body)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task MarkThreadReadAsync(long threadId)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task MarkAllThreadsReadAsync()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DeleteThreadAsync(long threadId)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DeleteMessagesAsync(IReadOnlyCollection<long> messageIds)
        {
            return Task.CompletedTask;
        }
    }
}
