// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of the clock tab, it stores the alarms, the world clock cities and the
    /// state of a running timer.
    /// </summary>
    [XmlRoot(ElementName = "anote_clock")]
    public class ClockDataModel
    {
#if RELEASE
        public const string FileName = "anote_clock.config";
#else
        public const string FileName = "anote_clock_dev.config";
#endif

        private List<AlarmModel> _alarms;
        private List<WorldClockCityModel> _cities;

        /// <summary>Gets or sets the configured alarms.</summary>
        [XmlArray("alarms")]
        [XmlArrayItem("alarm")]
        public List<AlarmModel> Alarms
        {
            get { return _alarms ?? (_alarms = new List<AlarmModel>()); }
            set { _alarms = value; }
        }

        /// <summary>Gets or sets the cities shown on the world clock.</summary>
        [XmlArray("cities")]
        [XmlArrayItem("city")]
        public List<WorldClockCityModel> Cities
        {
            get { return _cities ?? (_cities = new List<WorldClockCityModel>()); }
            set { _cities = value; }
        }

        /// <summary>Gets or sets the id of the timer which is currently running, or empty.</summary>
        [XmlAttribute(AttributeName = "timer_id")]
        public Guid TimerId { get; set; }

        /// <summary>Gets or sets the UTC time when the running timer will finish.</summary>
        [XmlAttribute(AttributeName = "timer_end_utc")]
        public DateTime TimerEndUtc { get; set; }

        /// <summary>Gets or sets the total duration of the running timer in seconds.</summary>
        [XmlAttribute(AttributeName = "timer_seconds")]
        public int TimerSeconds { get; set; }

        /// <summary>Gets or sets the label of the running timer.</summary>
        [XmlAttribute(AttributeName = "timer_label")]
        public string TimerLabel { get; set; }
    }

    /// <summary>
    /// A single alarm of the clock tab.
    /// </summary>
    public class AlarmModel
    {
        /// <summary>Gets or sets the id of the alarm.</summary>
        [XmlAttribute(AttributeName = "id")]
        public Guid Id { get; set; }

        /// <summary>Gets or sets the hour of the day (0-23).</summary>
        [XmlAttribute(AttributeName = "hour")]
        public int Hour { get; set; }

        /// <summary>Gets or sets the minute of the hour (0-59).</summary>
        [XmlAttribute(AttributeName = "minute")]
        public int Minute { get; set; }

        /// <summary>Gets or sets the label of the alarm.</summary>
        [XmlAttribute(AttributeName = "label")]
        public string Label { get; set; }

        /// <summary>
        /// Gets or sets the days on which the alarm repeats. It is a bit mask, bit 0 is Monday and
        /// bit 6 is Sunday. A value of 0 means the alarm fires on the next occurrence only.
        /// </summary>
        [XmlAttribute(AttributeName = "days")]
        public int Days { get; set; }

        /// <summary>Gets or sets a value indicating whether the alarm is active.</summary>
        [XmlAttribute(AttributeName = "enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>Gets a value indicating whether the alarm repeats on given weekdays.</summary>
        [XmlIgnore]
        public bool Repeats
        {
            get { return Days != 0; }
        }

        /// <summary>
        /// Determines the next local time when the alarm should fire.
        /// </summary>
        /// <param name="nowLocal">The current local time.</param>
        /// <returns>The local time of the next occurrence.</returns>
        public DateTime GetNextOccurrence(DateTime nowLocal)
        {
            DateTime candidate = nowLocal.Date.AddHours(Hour).AddMinutes(Minute);
            if (Days == 0)
                return (candidate <= nowLocal) ? candidate.AddDays(1) : candidate;

            for (int offset = 0; offset < 8; offset++)
            {
                DateTime day = candidate.AddDays(offset);
                int bit = ((int)day.DayOfWeek + 6) % 7; // Monday is bit 0
                if ((Days & (1 << bit)) == 0)
                    continue;
                if ((offset == 0) && (candidate <= nowLocal))
                    continue;
                return day;
            }
            return candidate.AddDays(1);
        }
    }

    /// <summary>
    /// A city shown on the world clock.
    /// </summary>
    public class WorldClockCityModel
    {
        /// <summary>Gets or sets the display name of the city.</summary>
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        /// <summary>Gets or sets the IANA id of the time zone, e.g. "Europe/Zurich".</summary>
        [XmlAttribute(AttributeName = "tz")]
        public string TimeZoneId { get; set; }
    }
}
