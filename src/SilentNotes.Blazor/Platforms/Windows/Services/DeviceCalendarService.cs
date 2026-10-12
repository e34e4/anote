// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IDeviceCalendarService"/> interface for the Windows
    /// platform. Windows does not provide an OS calendar which could be used here.
    /// </summary>
    internal class DeviceCalendarService : IDeviceCalendarService
    {
        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public bool HasPermission
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public Task<bool> RequestPermissionAsync()
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<DeviceCalendarModel>> GetCalendarsAsync()
        {
            return Task.FromResult<IReadOnlyList<DeviceCalendarModel>>(new List<DeviceCalendarModel>());
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<DeviceEventModel>> GetEventsAsync(DateTime fromLocal, DateTime toLocalExclusive)
        {
            return Task.FromResult<IReadOnlyList<DeviceEventModel>>(new List<DeviceEventModel>());
        }

        /// <inheritdoc/>
        public Task<DeviceEventModel> GetEventAsync(long eventId)
        {
            return Task.FromResult<DeviceEventModel>(null);
        }

        /// <inheritdoc/>
        public Task<DeviceEventModel> SaveEventAsync(DeviceEventModel model)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public Task DeleteEventAsync(long eventId)
        {
            return Task.CompletedTask;
        }
    }
}
