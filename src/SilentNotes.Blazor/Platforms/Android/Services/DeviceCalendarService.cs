// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.Provider;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IDeviceCalendarService"/> interface for the Android
    /// platform. It uses the system calendar provider, which also contains the calendars
    /// synchronized by a Google account.
    /// </summary>
    internal class DeviceCalendarService : IDeviceCalendarService
    {
        private const int CalendarAccessContributor = 500;

        // Column names of the calendar provider, used with a null projection and looked up by
        // name, so the service does not depend on a specific binding version.
        private const string ColumnId = "_id";
        private const string ColumnAccountName = "account_name";
        private const string ColumnCalendarDisplayName = "calendar_displayName";
        private const string ColumnCalendarColor = "calendar_color";
        private const string ColumnCalendarAccessLevel = "calendar_accessLevel";
        private const string ColumnEventId = "event_id";
        private const string ColumnCalendarId = "calendar_id";
        private const string ColumnBegin = "begin";
        private const string ColumnEnd = "end";
        private const string ColumnTitle = "title";
        private const string ColumnAllDay = "allDay";
        private const string ColumnDescription = "description";
        private const string ColumnEventLocation = "eventLocation";
        private const string ColumnDisplayColor = "displayColor";
        private const string ColumnEventColor = "eventColor";
        private const string ColumnDtStart = "dtstart";
        private const string ColumnDtEnd = "dtend";
        private const string ColumnEventTimezone = "eventTimezone";
        private const string ColumnMinutes = "minutes";
        private const string ColumnMethod = "method";

        private readonly IAppContextService _appContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceCalendarService"/> class.
        /// </summary>
        public DeviceCalendarService(IAppContextService appContextService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return true; }
        }

        /// <inheritdoc/>
        public bool HasPermission
        {
            get
            {
                Context context = _appContext.Context;
                if (context == null)
                    return false;
                return (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.ReadCalendar) == Permission.Granted)
                    && (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.WriteCalendar) == Permission.Granted);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestPermissionAsync()
        {
            try
            {
                PermissionStatus read = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.CalendarRead>());
                PermissionStatus write = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.CalendarWrite>());
                return (read == PermissionStatus.Granted) && (write == PermissionStatus.Granted);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<DeviceCalendarModel>> GetCalendarsAsync()
        {
            return Task.Run<IReadOnlyList<DeviceCalendarModel>>(() =>
            {
                var result = new List<DeviceCalendarModel>();
                if (!HasPermission)
                    return result;

                using ICursor cursor = _appContext.Context.ContentResolver.Query(
                    CalendarContract.Calendars.ContentUri, null, null, null, null);
                if (cursor == null)
                    return result;

                int idIndex = cursor.GetColumnIndex(ColumnId);
                int nameIndex = cursor.GetColumnIndex(ColumnCalendarDisplayName);
                int accountIndex = cursor.GetColumnIndex(ColumnAccountName);
                int colorIndex = cursor.GetColumnIndex(ColumnCalendarColor);
                int accessIndex = cursor.GetColumnIndex(ColumnCalendarAccessLevel);

                while (cursor.MoveToNext())
                {
                    var model = new DeviceCalendarModel
                    {
                        Id = GetLong(cursor, idIndex),
                        DisplayName = GetString(cursor, nameIndex),
                        AccountName = GetString(cursor, accountIndex),
                        ColorHex = ToColorHex(GetInt(cursor, colorIndex, 0)),
                        IsWritable = GetInt(cursor, accessIndex, 0) >= CalendarAccessContributor,
                    };
                    if (!string.IsNullOrWhiteSpace(model.DisplayName))
                        result.Add(model);
                }
                return result;
            });
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<DeviceEventModel>> GetEventsAsync(DateTime fromLocal, DateTime toLocalExclusive)
        {
            return Task.Run<IReadOnlyList<DeviceEventModel>>(() =>
            {
                var result = new List<DeviceEventModel>();
                if (!HasPermission)
                    return result;

                var builder = CalendarContract.Instances.ContentUri.BuildUpon();
                ContentUris.AppendId(builder, ToUnixMillis(fromLocal));
                ContentUris.AppendId(builder, ToUnixMillis(toLocalExclusive));

                using ICursor cursor = _appContext.Context.ContentResolver.Query(builder.Build(), null, null, null, null);
                if (cursor == null)
                    return result;

                int eventIdIndex = cursor.GetColumnIndex(ColumnEventId);
                int calendarIdIndex = cursor.GetColumnIndex(ColumnCalendarId);
                int beginIndex = cursor.GetColumnIndex(ColumnBegin);
                int endIndex = cursor.GetColumnIndex(ColumnEnd);
                int titleIndex = cursor.GetColumnIndex(ColumnTitle);
                int allDayIndex = cursor.GetColumnIndex(ColumnAllDay);
                int locationIndex = cursor.GetColumnIndex(ColumnEventLocation);
                int descriptionIndex = cursor.GetColumnIndex(ColumnDescription);
                int colorIndex = cursor.GetColumnIndex(ColumnDisplayColor);
                int eventColorIndex = cursor.GetColumnIndex(ColumnEventColor);

                while (cursor.MoveToNext())
                {
                    result.Add(new DeviceEventModel
                    {
                        Id = GetLong(cursor, eventIdIndex),
                        CalendarId = GetLong(cursor, calendarIdIndex),
                        StartUtc = FromUnixMillis(GetLong(cursor, beginIndex)),
                        EndUtc = FromUnixMillis(GetLong(cursor, endIndex)),
                        Title = GetString(cursor, titleIndex),
                        AllDay = GetInt(cursor, allDayIndex, 0) != 0,
                        Location = GetString(cursor, locationIndex),
                        Description = GetString(cursor, descriptionIndex),
                        ColorHex = ReadColorHex(cursor, eventColorIndex, colorIndex),
                    });
                }

                AttachReminders(result);
                return result;
            });
        }

        /// <inheritdoc/>
        public Task<DeviceEventModel> GetEventAsync(long eventId)
        {
            return Task.Run(() =>
            {
                if (!HasPermission || (eventId == 0))
                    return null;

                using ICursor cursor = _appContext.Context.ContentResolver.Query(
                    ContentUris.WithAppendedId(CalendarContract.Events.ContentUri, eventId), null, null, null, null);
                if ((cursor == null) || !cursor.MoveToFirst())
                    return null;

                var model = new DeviceEventModel
                {
                    Id = eventId,
                    CalendarId = GetLong(cursor, cursor.GetColumnIndex(ColumnCalendarId)),
                    Title = GetString(cursor, cursor.GetColumnIndex(ColumnTitle)),
                    Description = GetString(cursor, cursor.GetColumnIndex(ColumnDescription)),
                    Location = GetString(cursor, cursor.GetColumnIndex(ColumnEventLocation)),
                    AllDay = GetInt(cursor, cursor.GetColumnIndex(ColumnAllDay), 0) != 0,
                    StartUtc = FromUnixMillis(GetLong(cursor, cursor.GetColumnIndex(ColumnDtStart))),
                    EndUtc = FromUnixMillis(GetLong(cursor, cursor.GetColumnIndex(ColumnDtEnd))),
                    ColorHex = ReadColorHex(cursor, cursor.GetColumnIndex(ColumnEventColor), cursor.GetColumnIndex(ColumnDisplayColor)),
                };

                AttachReminders(new List<DeviceEventModel> { model });
                return model;
            });
        }

        /// <inheritdoc/>
        public Task<DeviceEventModel> SaveEventAsync(DeviceEventModel model)
        {
            return Task.Run(() =>
            {
                ContentResolver resolver = _appContext.Context.ContentResolver;
                var values = new ContentValues();
                values.Put(ColumnCalendarId, model.CalendarId);
                values.Put(ColumnTitle, model.Title ?? string.Empty);
                values.Put(ColumnDescription, model.Description ?? string.Empty);
                values.Put(ColumnEventLocation, model.Location ?? string.Empty);
                values.Put(ColumnAllDay, model.AllDay ? 1 : 0);
                values.Put(ColumnDtStart, ToUnixMillis(model.StartUtc));
                values.Put(ColumnDtEnd, ToUnixMillis(model.EndUtc));
                values.Put(ColumnEventTimezone, model.AllDay ? "UTC" : TimeZoneInfo.Local.Id);

                int color = ParseColorHex(model.ColorHex);
                if (color != 0)
                    values.Put(ColumnEventColor, color);

                long eventId;
                if (model.Id == 0)
                {
                    var uri = resolver.Insert(CalendarContract.Events.ContentUri, values);
                    eventId = (uri != null) && long.TryParse(uri.LastPathSegment, out long parsedId) ? parsedId : 0;
                }
                else
                {
                    eventId = model.Id;
                    resolver.Update(ContentUris.WithAppendedId(CalendarContract.Events.ContentUri, eventId), values, null, null);
                }

                if (eventId != 0)
                {
                    // Replace the reminders of the event with the edited list.
                    resolver.Delete(CalendarContract.Reminders.ContentUri, ColumnEventId + "=?", new[] { eventId.ToString(CultureInfo.InvariantCulture) });
                    foreach (int minutes in model.Reminders ?? new List<int>())
                    {
                        if (minutes < 0)
                            continue;
                        var reminderValues = new ContentValues();
                        reminderValues.Put(ColumnEventId, eventId);
                        reminderValues.Put(ColumnMinutes, minutes);
                        reminderValues.Put(ColumnMethod, 1 /* alert */);
                        resolver.Insert(CalendarContract.Reminders.ContentUri, reminderValues);
                    }
                }

                model.Id = eventId;
                return model;
            });
        }

        /// <inheritdoc/>
        public Task DeleteEventAsync(long eventId)
        {
            return Task.Run(() =>
            {
                _appContext.Context.ContentResolver.Delete(
                    ContentUris.WithAppendedId(CalendarContract.Events.ContentUri, eventId), null, null);
            });
        }

        private void AttachReminders(List<DeviceEventModel> events)
        {
            if (events.Count == 0)
                return;

            var eventIds = new HashSet<long>();
            foreach (DeviceEventModel item in events)
                eventIds.Add(item.Id);
            if (eventIds.Count == 0)
                return;

            string selection = ColumnEventId + " IN (" + string.Join(",", eventIds) + ")";
            using ICursor cursor = _appContext.Context.ContentResolver.Query(
                CalendarContract.Reminders.ContentUri, null, selection, null, null);
            if (cursor == null)
                return;

            int eventIdIndex = cursor.GetColumnIndex(ColumnEventId);
            int minutesIndex = cursor.GetColumnIndex(ColumnMinutes);
            var reminders = new Dictionary<long, List<int>>();
            while (cursor.MoveToNext())
            {
                long eventId = GetLong(cursor, eventIdIndex);
                int minutes = GetInt(cursor, minutesIndex, -1);
                if (minutes < 0)
                    continue;
                if (!reminders.TryGetValue(eventId, out List<int> list))
                {
                    list = new List<int>();
                    reminders[eventId] = list;
                }
                list.Add(minutes);
            }

            foreach (DeviceEventModel item in events)
            {
                if (reminders.TryGetValue(item.Id, out List<int> list))
                    item.Reminders = list.Distinct().OrderBy(minutes => minutes).ToList();
            }
        }

        private static string ReadColorHex(ICursor cursor, int eventColorIndex, int displayColorIndex)
        {
            int color = GetInt(cursor, eventColorIndex, 0);
            if (color == 0)
                color = GetInt(cursor, displayColorIndex, 0);
            return (color == 0) ? null : ToColorHex(color);
        }

        private static int ParseColorHex(string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex))
                return 0;
            string hex = colorHex.TrimStart('#');
            if ((hex.Length == 6) && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return unchecked((int)0xFF000000) | rgb;
            return 0;
        }

        private static long ToUnixMillis(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        }

        private static DateTime FromUnixMillis(long millis)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
        }

        private static string ToColorHex(int argb)
        {
            return "#" + (argb & 0x00FFFFFF).ToString("X6", CultureInfo.InvariantCulture);
        }

        private static long GetLong(ICursor cursor, int index)
        {
            return (index >= 0) ? cursor.GetLong(index) : 0;
        }

        private static int GetInt(ICursor cursor, int index, int defaultValue)
        {
            return (index >= 0) ? cursor.GetInt(index) : defaultValue;
        }

        private static string GetString(ICursor cursor, int index)
        {
            return (index >= 0) ? cursor.GetString(index) : null;
        }
    }
}
