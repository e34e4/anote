// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// Local Abro browser settings: search engines and custom font.
    /// </summary>
    public class AbroBrowserSettingsModel
    {
        /// <summary>Well-known intercept URL used to serve the copied custom font to pages.</summary>
        public const string CustomFontInterceptUrl = "https://abro.internal/custom-font";

        /// <summary>CSS / @font-face family name for the custom font.</summary>
        public const string CustomFontFamilyName = "AbroCustom";

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroBrowserSettingsModel"/> class.
        /// </summary>
        public AbroBrowserSettingsModel()
        {
            Version = 1;
            UpdatedAt = DateTime.UtcNow;
            SearchSuggestionsEnabled = true;
            Engines = new List<AbroSearchEngineModel>();
            UseCustomFont = false;
            CustomFontFileName = string.Empty;
        }

        /// <summary>Gets or sets the store format version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last change.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>Gets or sets whether address-bar search suggestions are enabled.</summary>
        public bool SearchSuggestionsEnabled { get; set; }

        /// <summary>Gets or sets the id of the default search engine.</summary>
        public Guid? DefaultEngineId { get; set; }

        /// <summary>Gets or sets the configured search engines.</summary>
        public List<AbroSearchEngineModel> Engines { get; set; }

        /// <summary>Gets or sets whether pages should use the copied custom TTF.</summary>
        public bool UseCustomFont { get; set; }

        /// <summary>
        /// Gets or sets the file name of the copied font under <c>abro/fonts/</c>
        /// (e.g. <c>custom.ttf</c>). Empty when using the system font.
        /// </summary>
        public string CustomFontFileName { get; set; }

        /// <summary>
        /// Touches <see cref="UpdatedAt"/> to now (UTC).
        /// </summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Returns the default engine, falling back to the first entry, or null if empty.
        /// </summary>
        public AbroSearchEngineModel GetDefaultEngine()
        {
            if (Engines == null || Engines.Count == 0)
                return null;

            if (DefaultEngineId.HasValue)
            {
                AbroSearchEngineModel match = Engines.Find(e => e.Id == DefaultEngineId.Value);
                if (match != null)
                    return match;
            }

            return Engines[0];
        }

        /// <summary>
        /// Seeds built-in engines when the list is empty.
        /// </summary>
        public void EnsureDefaultEngines()
        {
            if (Engines == null)
                Engines = new List<AbroSearchEngineModel>();
            if (Engines.Count > 0)
                return;

            var ddg = new AbroSearchEngineModel
            {
                Name = "DuckDuckGo",
                SearchUrl = "https://duckduckgo.com/?q={query}",
                SuggestUrl = "https://duckduckgo.com/ac/?q={query}&type=list",
            };
            var google = new AbroSearchEngineModel
            {
                Name = "Google",
                SearchUrl = "https://www.google.com/search?q={query}",
                SuggestUrl = "https://suggestqueries.google.com/complete/search?client=firefox&q={query}",
            };
            var bing = new AbroSearchEngineModel
            {
                Name = "Bing",
                SearchUrl = "https://www.bing.com/search?q={query}",
                SuggestUrl = "https://api.bing.com/osjson.aspx?query={query}",
            };
            Engines.Add(ddg);
            Engines.Add(google);
            Engines.Add(bing);
            DefaultEngineId = ddg.Id;
        }
    }
}
