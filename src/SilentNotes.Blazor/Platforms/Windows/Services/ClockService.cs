// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IClockService"/> interface for the Windows platform.
    /// Windows cannot schedule system alarms, so only the world clock and the stopwatch are
    /// available, the clock data is still persisted.
    /// </summary>
    internal class ClockService : ClockServiceBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClockService"/> class.
        /// </summary>
        public ClockService(IXmlFileService xmlFileService)
            : base(xmlFileService)
        {
        }

        /// <inheritdoc/>
        protected override string GetLocation()
        {
            return Path.Combine(FileSystem.Current.AppDataDirectory, "clock");
        }

        /// <inheritdoc/>
        public override bool IsSupported
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public override bool HasNotificationPermission
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public override Task<bool> RequestNotificationPermissionAsync()
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public override bool CanScheduleExactAlarms
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public override Task<bool> RequestExactAlarmPermissionAsync()
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public override Task ScheduleAlarmAsync(AlarmModel alarm)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task CancelAlarmAsync(Guid alarmId)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task ScheduleTimerAsync(Guid timerId, DateTime fireAtUtc, int totalSeconds, string label)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task CancelTimerAsync(Guid timerId)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override Task RescheduleAllAsync()
        {
            return Task.CompletedTask;
        }
    }
}
