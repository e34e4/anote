// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
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
        }

        /// <summary>Gets or sets the role: user, assistant or system.</summary>
        public string Role { get; set; }

        /// <summary>Gets or sets the message text.</summary>
        public string Content { get; set; }

        /// <summary>Gets or sets the UTC timestamp when the message was created.</summary>
        public DateTime At { get; set; }
    }
}
