// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Gives access to the calendars of the operating system, so events and their reminders can be
    /// shown and edited. On Android this is the system calendar provider, which also holds the
    /// calendars synchronized by a Google account.
    /// </summary>
    public interface IDeviceCalendarService
    {
        /// <summary>Gets a value indicating whether the platform supports device calendars.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether the app may read and write the calendars.</summary>
        bool HasPermission { get; }

        /// <summary>Asks the user for the calendar permissions.</summary>
        /// <returns>True if the permission was granted, otherwise false.</returns>
        Task<bool> RequestPermissionAsync();

        /// <summary>Gets the calendars which are available on the device.</summary>
        Task<IReadOnlyList<DeviceCalendarModel>> GetCalendarsAsync();

        /// <summary>
        /// Gets the events which overlap the given local time range. Recurring events are expanded
        /// into single occurrences.
        /// </summary>
        /// <param name="fromLocal">Start of the range (inclusive), local time.</param>
        /// <param name="toLocalExclusive">End of the range (exclusive), local time.</param>
        Task<IReadOnlyList<DeviceEventModel>> GetEventsAsync(DateTime fromLocal, DateTime toLocalExclusive);

        /// <summary>Gets a single event by its id.</summary>
        /// <param name="eventId">The id of the event.</param>
        /// <returns>The event, or null if it does not exist.</returns>
        Task<DeviceEventModel> GetEventAsync(long eventId);

        /// <summary>
        /// Inserts a new event or updates an existing one, together with its reminder.
        /// </summary>
        /// <param name="model">The event to store. A new event is created for id 0.</param>
        /// <returns>The stored event, including its assigned id.</returns>
        Task<DeviceEventModel> SaveEventAsync(DeviceEventModel model);

        /// <summary>Deletes an event from the device calendar.</summary>
        /// <param name="eventId">The id of the event to delete.</param>
        Task DeleteEventAsync(long eventId);
    }
}
