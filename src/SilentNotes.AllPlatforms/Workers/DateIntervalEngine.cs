// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Date interval calculations shared by the calendar date tools and the calculator date mode.
    /// </summary>
    public static class DateIntervalEngine
    {
        /// <summary>
        /// Describes the interval between two dates.
        /// </summary>
        public static string Describe(DateTime from, DateTime to)
        {
            if (to < from)
            {
                DateTime swap = from;
                from = to;
                to = swap;
            }

            TimeSpan span = to - from;
            int totalDays = (int)span.TotalDays;
            double weeks = totalDays / 7.0;
            int months = ((to.Year - from.Year) * 12) + (to.Month - from.Month);
            if (to.Day < from.Day)
                months--;
            if (months < 0)
                months = 0;
            double years = months / 12.0;

            const string format = "yyyy-MM-dd HH:mm";
            return string.Join("\n", new[]
            {
                from.ToString(format, CultureInfo.InvariantCulture) + " → " + to.ToString(format, CultureInfo.InvariantCulture),
                "Days: " + totalDays.ToString(CultureInfo.InvariantCulture),
                "Weeks: " + weeks.ToString("0.##", CultureInfo.InvariantCulture),
                "Months: " + months.ToString(CultureInfo.InvariantCulture),
                "Years: " + years.ToString("0.##", CultureInfo.InvariantCulture),
                "Hours: " + span.TotalHours.ToString("0.##", CultureInfo.InvariantCulture),
                "Minutes: " + span.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture),
                "Seconds: " + span.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture),
            });
        }

        /// <summary>
        /// Adds or subtracts an interval from a date. Supported units are d, w, mo, y, h, min, s.
        /// </summary>
        public static DateTime? Shift(DateTime start, double amount, string unit)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount))
                return null;

            switch ((unit ?? "d").Trim().ToLowerInvariant())
            {
                case "s":
                case "sec":
                    return start.AddSeconds(amount);
                case "min":
                case "m":
                    return start.AddMinutes(amount);
                case "h":
                case "hour":
                    return start.AddHours(amount);
                case "d":
                case "day":
                    return start.AddDays(amount);
                case "w":
                case "week":
                    return start.AddDays(amount * 7.0);
                case "mo":
                case "month":
                    return start.AddMonths((int)Math.Round(amount));
                case "y":
                case "year":
                    return start.AddYears((int)Math.Round(amount));
                default:
                    return null;
            }
        }

        /// <summary>
        /// Parses a date entered in a date or datetime-local input.
        /// </summary>
        public static bool TryParseDate(string text, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] formats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
                return true;
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date))
                return true;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
        }

        /// <summary>
        /// Formats a date for a datetime-local input.
        /// </summary>
        public static string ToInputValue(DateTime date)
        {
            return date.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        }
    }
}
