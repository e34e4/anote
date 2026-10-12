// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// A calendar which is provided by the operating system, e.g. a Google calendar which is
    /// synchronized by the Android system.
    /// </summary>
    public class DeviceCalendarModel
    {
        /// <summary>Gets or sets the id of the calendar.</summary>
        public long Id { get; set; }

        /// <summary>Gets or sets the display name of the calendar.</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the account which owns the calendar.</summary>
        public string AccountName { get; set; }

        /// <summary>Gets or sets the color of the calendar as "#RRGGBB".</summary>
        public string ColorHex { get; set; }

        /// <summary>Gets or sets a value indicating whether events can be added to this calendar.</summary>
        public bool IsWritable { get; set; }

        /// <summary>Gets the label shown in the calendar selection.</summary>
        public string DisplayLabel
        {
            get
            {
                return string.IsNullOrWhiteSpace(AccountName) || (AccountName == DisplayName)
                    ? DisplayName
                    : string.Format("{0} ({1})", DisplayName, AccountName);
            }
        }
    }
}
