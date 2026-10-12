// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// An event of a device calendar. The times are always stored in UTC, the local time is
    /// calculated from them when the event is displayed.
    /// </summary>
    public class DeviceEventModel
    {
        /// <summary>Gets or sets the id of the event, 0 for a new event.</summary>
        public long Id { get; set; }

        /// <summary>Gets or sets the id of the calendar which owns the event.</summary>
        public long CalendarId { get; set; }

        /// <summary>Gets or sets the title of the event.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the description of the event.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the location of the event.</summary>
        public string Location { get; set; }

        /// <summary>Gets or sets the start time in UTC.</summary>
        public DateTime StartUtc { get; set; }

        /// <summary>Gets or sets the end time in UTC.</summary>
        public DateTime EndUtc { get; set; }

        /// <summary>Gets or sets a value indicating whether this is an all day event.</summary>
        public bool AllDay { get; set; }

        /// <summary>
        /// Gets or sets the reminders, each one is the number of minutes before the event starts.
        /// </summary>
        public List<int> Reminders { get; set; } = new List<int>();

        /// <summary>Gets or sets the color of the event as "#RRGGBB", null to use the calendar color.</summary>
        public string ColorHex { get; set; }
    }
}
