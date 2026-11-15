// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Reads and writes the Abro <c>tabs.json</c> file, locally and on WebDAV.
    /// </summary>
    public static class AbroTabsFormat
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        /// <summary>Parses the tabs JSON, returns an empty state for blank input or null if invalid.</summary>
        public static AbroTabsStateModel Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AbroTabsStateModel();

            try
            {
                AbroTabsStateModel state = JsonSerializer.Deserialize<AbroTabsStateModel>(json, Options);
                if (state == null)
                    return null;

                state.Tabs ??= new List<string>();
                state.Tabs.RemoveAll(string.IsNullOrWhiteSpace);
                state.Selected = Math.Clamp(state.Selected, 0, Math.Max(0, state.Tabs.Count - 1));
                return state;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Serializes the tab state.</summary>
        public static string Serialize(AbroTabsStateModel state)
        {
            state ??= new AbroTabsStateModel();
            var tabs = new JsonArray();
            if (state.Tabs != null)
            {
                foreach (string url in state.Tabs)
                {
                    if (!string.IsNullOrWhiteSpace(url))
                        tabs.Add(url);
                }
            }

            var root = new JsonObject
            {
                ["Selected"] = state.Selected,
                ["Tabs"] = tabs,
            };
            return root.ToJsonString(Options);
        }

        /// <summary>
        /// Merges two tab states: remote tabs first, then local tabs, deduplicated by url.
        /// </summary>
        public static AbroTabsStateModel Merge(AbroTabsStateModel local, AbroTabsStateModel remote)
        {
            var result = new AbroTabsStateModel();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AbroTabsStateModel source in new[] { remote, local })
            {
                if (source?.Tabs == null)
                    continue;
                foreach (string url in source.Tabs)
                {
                    if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                        continue;
                    result.Tabs.Add(url);
                    if (result.Tabs.Count >= 24)
                        break;
                }
            }

            if (result.Tabs.Count == 0)
                result.Tabs.Add("https://duckduckgo.com");

            int selected = local?.Selected ?? 0;
            result.Selected = Math.Clamp(selected, 0, result.Tabs.Count - 1);
            return result;
        }
    }
}
