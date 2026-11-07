// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// Image bytes attached to a chat turn (OpenAI vision / multimodal content).
    /// Not persisted in the Abro store JSON — kept in-memory for the API request only.
    /// </summary>
    public class AbroAiImageAttachment
    {
        public string FileName { get; set; }
        public string MimeType { get; set; }
        public string Base64Data { get; set; }
    }

    /// <summary>
    /// A single chat message in an Abro AI conversation.
    /// </summary>
    public class AbroAiMessageModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiMessageModel"/> class.
        /// </summary>
        public AbroAiMessageModel()
        {
            Role = "user";
            Content = string.Empty;
            At = DateTime.UtcNow;
            Images = new List<AbroAiImageAttachment>();
        }

        /// <summary>Gets or sets the role: user, assistant or system.</summary>
        public string Role { get; set; }

        /// <summary>Gets or sets the message text.</summary>
        public string Content { get; set; }

        /// <summary>
        /// Gets or sets optional image attachments for multimodal / vision requests.
        /// </summary>
        public List<AbroAiImageAttachment> Images { get; set; }

        /// <summary>Gets or sets the UTC timestamp when the message was created.</summary>
        public DateTime At { get; set; }
    }
}
