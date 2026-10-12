// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Gives the clock tab access to the alarms and timers of the operating system. On Android the
    /// alarms are scheduled with the AlarmManager, so they ring even when the app is closed.
    /// </summary>
    public interface IClockService
    {
        /// <summary>Gets a value indicating whether the platform can schedule system alarms.</summary>
        bool IsSupported { get; }

        /// <summary>Loads the stored clock data (alarms, cities and running timer).</summary>
        ClockDataModel LoadData();

        /// <summary>Saves the clock data.</summary>
        /// <param name="model">The data to store.</param>
        /// <returns>True if the data could be stored, otherwise false.</returns>
        bool TrySaveData(ClockDataModel model);

        /// <summary>Gets a value indicating whether notifications are allowed by the user.</summary>
        bool HasNotificationPermission { get; }

        /// <summary>Asks the user for the notification permission.</summary>
        /// <returns>True if the permission was granted, otherwise false.</returns>
        Task<bool> RequestNotificationPermissionAsync();

        /// <summary>
        /// Gets a value indicating whether the app is allowed to schedule exact alarms. Only
        /// relevant on Android 12 and above.
        /// </summary>
        bool CanScheduleExactAlarms { get; }

        /// <summary>Opens the system settings where the user can allow exact alarms.</summary>
        /// <returns>True if the permission was granted, otherwise false.</returns>
        Task<bool> RequestExactAlarmPermissionAsync();

        /// <summary>Schedules (or reschedules) an alarm with the operating system.</summary>
        /// <param name="alarm">The alarm to schedule, it must be enabled.</param>
        Task ScheduleAlarmAsync(AlarmModel alarm);

        /// <summary>Cancels a scheduled alarm.</summary>
        /// <param name="alarmId">The id of the alarm to cancel.</param>
        Task CancelAlarmAsync(Guid alarmId);

        /// <summary>Schedules a timer which fires at a given UTC time.</summary>
        Task ScheduleTimerAsync(Guid timerId, DateTime fireAtUtc, int totalSeconds, string label);

        /// <summary>Cancels a scheduled timer.</summary>
        Task CancelTimerAsync(Guid timerId);

        /// <summary>
        /// Reschedules all enabled alarms and a still running timer, this is called after a reboot
        /// or when the clock data changed.
        /// </summary>
        Task RescheduleAllAsync();
    }
}
