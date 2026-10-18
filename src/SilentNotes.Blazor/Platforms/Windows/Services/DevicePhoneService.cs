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
    /// Stub implementation of <see cref="IDevicePhoneService"/> for Windows.
    /// </summary>
    internal class DevicePhoneService : IDevicePhoneService
    {
        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public bool HasPermissions => false;

        /// <inheritdoc/>
        public Task<bool> RequestPermissionsAsync() => Task.FromResult(false);

        /// <inheritdoc/>
        public bool IsDefaultDialerApp => false;

        /// <inheritdoc/>
        public Task<bool> RequestDefaultDialerAppAsync() => Task.FromResult(false);

        /// <inheritdoc/>
        public Task<IReadOnlyList<CallLogEntryModel>> GetCallLogAsync() =>
            Task.FromResult<IReadOnlyList<CallLogEntryModel>>(new List<CallLogEntryModel>());

        /// <inheritdoc/>
        public Task DeleteCallLogEntryAsync(long id) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task ClearCallLogAsync() => Task.CompletedTask;

        /// <inheritdoc/>
        public Task<bool> PlaceCallAsync(string number) => Task.FromResult(false);

        /// <inheritdoc/>
        public ActiveCallModel GetActiveCall() => null;

        /// <inheritdoc/>
        public void AnswerCall()
        {
        }

        /// <inheritdoc/>
        public void HangupCall()
        {
        }

        /// <inheritdoc/>
        public void ToggleMute()
        {
        }

        /// <inheritdoc/>
        public void ToggleSpeaker()
        {
        }

        /// <inheritdoc/>
        public void SendDtmf(char digit)
        {
        }
    }
}
