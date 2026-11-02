// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// OpenAI-compatible API provider used by the Abro AI chat.
    /// </summary>
    public class AbroAiProviderModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiProviderModel"/> class.
        /// </summary>
        public AbroAiProviderModel()
        {
            Id = Guid.NewGuid();
            ModifiedAt = DateTime.UtcNow;
            Name = string.Empty;
            Url = string.Empty;
            ModelId = string.Empty;
            ApiKey = string.Empty;
        }

        /// <summary>Gets or sets the unique id of the provider.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the API base url, e.g. <c>https://api.commandcode.ai/provider/v1</c>.
        /// </summary>
        public string Url { get; set; }

        /// <summary>Gets or sets the model id sent to the API, e.g. <c>deepseek/deepseek-v4.1-flash</c>.</summary>
        public string ModelId { get; set; }

        /// <summary>Gets or sets the API key (Bearer token).</summary>
        public string ApiKey { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last modification.</summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Copies all fields onto <paramref name="target"/>.
        /// </summary>
        public void CloneTo(AbroAiProviderModel target)
        {
            target.Id = Id;
            target.Name = Name;
            target.Url = Url;
            target.ModelId = ModelId;
            target.ApiKey = ApiKey;
            target.ModifiedAt = ModifiedAt;
        }
    }
}
