// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// A curated list of cities which can be shown on the world clock, together with the default
    /// selection.
    /// </summary>
    public static class WorldClockCatalog
    {
        private static readonly List<WorldClockCityModel> AllCities = new List<WorldClockCityModel>
        {
            new WorldClockCityModel { Name = "London", TimeZoneId = "Europe/London" },
            new WorldClockCityModel { Name = "Paris", TimeZoneId = "Europe/Paris" },
            new WorldClockCityModel { Name = "Berlin", TimeZoneId = "Europe/Berlin" },
            new WorldClockCityModel { Name = "Rome", TimeZoneId = "Europe/Rome" },
            new WorldClockCityModel { Name = "Madrid", TimeZoneId = "Europe/Madrid" },
            new WorldClockCityModel { Name = "Zurich", TimeZoneId = "Europe/Zurich" },
            new WorldClockCityModel { Name = "Moscow", TimeZoneId = "Europe/Moscow" },
            new WorldClockCityModel { Name = "Istanbul", TimeZoneId = "Europe/Istanbul" },
            new WorldClockCityModel { Name = "Dubai", TimeZoneId = "Asia/Dubai" },
            new WorldClockCityModel { Name = "Mumbai", TimeZoneId = "Asia/Kolkata" },
            new WorldClockCityModel { Name = "Bangkok", TimeZoneId = "Asia/Bangkok" },
            new WorldClockCityModel { Name = "Singapore", TimeZoneId = "Asia/Singapore" },
            new WorldClockCityModel { Name = "Hong Kong", TimeZoneId = "Asia/Hong_Kong" },
            new WorldClockCityModel { Name = "Shanghai", TimeZoneId = "Asia/Shanghai" },
            new WorldClockCityModel { Name = "Tokyo", TimeZoneId = "Asia/Tokyo" },
            new WorldClockCityModel { Name = "Seoul", TimeZoneId = "Asia/Seoul" },
            new WorldClockCityModel { Name = "Sydney", TimeZoneId = "Australia/Sydney" },
            new WorldClockCityModel { Name = "Auckland", TimeZoneId = "Pacific/Auckland" },
            new WorldClockCityModel { Name = "Cairo", TimeZoneId = "Africa/Cairo" },
            new WorldClockCityModel { Name = "Johannesburg", TimeZoneId = "Africa/Johannesburg" },
            new WorldClockCityModel { Name = "Lagos", TimeZoneId = "Africa/Lagos" },
            new WorldClockCityModel { Name = "Sao Paulo", TimeZoneId = "America/Sao_Paulo" },
            new WorldClockCityModel { Name = "Buenos Aires", TimeZoneId = "America/Argentina/Buenos_Aires" },
            new WorldClockCityModel { Name = "New York", TimeZoneId = "America/New_York" },
            new WorldClockCityModel { Name = "Chicago", TimeZoneId = "America/Chicago" },
            new WorldClockCityModel { Name = "Denver", TimeZoneId = "America/Denver" },
            new WorldClockCityModel { Name = "Los Angeles", TimeZoneId = "America/Los_Angeles" },
            new WorldClockCityModel { Name = "Mexico City", TimeZoneId = "America/Mexico_City" },
            new WorldClockCityModel { Name = "Honolulu", TimeZoneId = "Pacific/Honolulu" },
        };

        /// <summary>Gets all cities which can be added to the world clock.</summary>
        public static IReadOnlyList<WorldClockCityModel> Cities
        {
            get { return AllCities; }
        }

        /// <summary>
        /// Creates the default selection of cities, the local time zone is always the first entry.
        /// </summary>
        /// <returns>The default cities.</returns>
        public static List<WorldClockCityModel> CreateDefaults()
        {
            var result = new List<WorldClockCityModel>
            {
                new WorldClockCityModel { Name = GetLocalCityName(), TimeZoneId = TimeZoneInfo.Local.Id },
            };

            foreach (string id in new[] { "Europe/London", "America/New_York", "Asia/Tokyo" })
            {
                WorldClockCityModel city = AllCities.Find(c => c.TimeZoneId == id);
                if (city != null)
                    result.Add(new WorldClockCityModel { Name = city.Name, TimeZoneId = city.TimeZoneId });
            }
            return result;
        }

        private static string GetLocalCityName()
        {
            string id = TimeZoneInfo.Local.Id;
            int slash = id.LastIndexOf('/');
            return (slash >= 0) ? id.Substring(slash + 1).Replace('_', ' ') : id;
        }
    }
}
