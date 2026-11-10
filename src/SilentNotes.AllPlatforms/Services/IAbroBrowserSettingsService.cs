// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Loads Abro search-engine and font settings, and serves suggestion / font helpers.
    /// </summary>
    public interface IAbroBrowserSettingsService
    {
        /// <summary>Gets the absolute path of the settings JSON file.</summary>
        string StoreFilePath { get; }

        /// <summary>Gets the directory where custom fonts are stored.</summary>
        string FontsDirectory { get; }

        /// <summary>Loads settings from disk (with built-in engines if empty).</summary>
        AbroBrowserSettingsModel LoadOrDefault();

        /// <summary>Saves settings to disk.</summary>
        bool TrySave(AbroBrowserSettingsModel settings);

        /// <summary>Builds a search URL for the default engine and query text.</summary>
        string BuildSearchUrl(string query);

        /// <summary>Gets whether search suggestions are enabled.</summary>
        bool SearchSuggestionsEnabled { get; }

        /// <summary>Fetches suggestion phrases for the address bar.</summary>
        Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default);

        /// <summary>Gets whether a custom font should be injected into pages.</summary>
        bool UseCustomFont { get; }

        /// <summary>
        /// Returns whether the forced black / white theme should apply for the given page URL.
        /// </summary>
        bool ShouldApplyBlackMode(string pageUrl);

        /// <summary>
        /// Returns whether image blocking should apply for the given page URL.
        /// </summary>
        bool ShouldApplyBlockImages(string pageUrl);

        /// <summary>Gets the absolute path of the copied custom font file, or null.</summary>
        string GetCustomFontPath();

        /// <summary>
        /// Copies font bytes into the Abro fonts folder and enables the custom font.
        /// </summary>
        bool TryInstallCustomFont(byte[] fontBytes, string originalFileName);

        /// <summary>Clears the custom font and switches back to the system font.</summary>
        bool ClearCustomFont();
    }
}
