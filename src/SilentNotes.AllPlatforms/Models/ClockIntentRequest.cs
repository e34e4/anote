// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// The action which was requested by an incoming clock intent.
    /// </summary>
    public enum ClockIntentAction
    {
        /// <summary>Open the clock tab on the alarm section, showing the ringing alarm.</summary>
        OpenAlarm,

        /// <summary>Open the clock tab on the timer section.</summary>
        OpenTimer,
    }

    /// <summary>
    /// Describes a clock intent (e.g. "the alarm is ringing, open the alarm section") which was
    /// triggered by the alarm notification. A request which was not handled yet is kept in
    /// <see cref="Pending"/>, so it survives until the clock tab is opened.
    /// </summary>
    public class ClockIntentRequest
    {
        /// <summary>
        /// Gets or sets the clock intent which was received but not handled yet. It is set by the
        /// Android alarm receiver and cleared as soon as the clock tab has applied it.
        /// </summary>
        public static ClockIntentRequest Pending { get; set; }

        /// <summary>Gets or sets the requested action.</summary>
        public ClockIntentAction Action { get; set; }

        /// <summary>Gets or sets the id of the affected alarm, empty for a timer.</summary>
        public Guid AlarmId { get; set; }
    }
}
