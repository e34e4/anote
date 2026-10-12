// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// The action which was requested by an incoming calendar intent.
    /// </summary>
    public enum CalendarIntentAction
    {
        /// <summary>Show the calendar at a given day.</summary>
        OpenDate,

        /// <summary>Show the day which contains a given event.</summary>
        OpenEvent,

        /// <summary>Open the editor for an existing event.</summary>
        EditEvent,

        /// <summary>Open the editor for a new, prefilled event.</summary>
        NewEvent,
    }

    /// <summary>
    /// Describes a calendar intent (e.g. "open the calendar at this date" or "add this event")
    /// which was sent by another application. A request which was not handled yet is kept in
    /// <see cref="Pending"/>, so it survives until the calendar tab is opened.
    /// </summary>
    public class CalendarIntentRequest
    {
        /// <summary>
        /// Gets or sets the calendar intent which was received but not handled yet. It is set by
        /// the Android activity and cleared as soon as the calendar tab has applied it.
        /// </summary>
        public static CalendarIntentRequest Pending { get; set; }

        /// <summary>Gets or sets the requested action.</summary>
        public CalendarIntentAction Action { get; set; }

        /// <summary>Gets or sets the day to show (local date).</summary>
        public DateTime? Date { get; set; }

        /// <summary>Gets or sets the id of the affected event.</summary>
        public long EventId { get; set; }

        /// <summary>Gets or sets the title of a prefilled event.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets the description of a prefilled event.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the location of a prefilled event.</summary>
        public string Location { get; set; }

        /// <summary>Gets or sets the start time of a prefilled event, in UTC.</summary>
        public DateTime? BeginUtc { get; set; }

        /// <summary>Gets or sets the end time of a prefilled event, in UTC.</summary>
        public DateTime? EndUtc { get; set; }

        /// <summary>Gets or sets a value indicating whether a prefilled event lasts all day.</summary>
        public bool AllDay { get; set; }
    }
}
