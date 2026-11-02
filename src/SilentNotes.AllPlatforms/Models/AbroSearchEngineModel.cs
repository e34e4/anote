// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// A configurable Abro search engine.
    /// </summary>
    public class AbroSearchEngineModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroSearchEngineModel"/> class.
        /// </summary>
        public AbroSearchEngineModel()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            SearchUrl = string.Empty;
            SuggestUrl = string.Empty;
        }

        /// <summary>Gets or sets the unique id.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the search URL template. Use <c>{query}</c> as the placeholder.
        /// Example: <c>https://duckduckgo.com/?q={query}</c>.
        /// </summary>
        public string SearchUrl { get; set; }

        /// <summary>
        /// Gets or sets an optional suggestions URL template with <c>{query}</c>.
        /// Empty means suggestions are unavailable for this engine.
        /// </summary>
        public string SuggestUrl { get; set; }
    }
}
