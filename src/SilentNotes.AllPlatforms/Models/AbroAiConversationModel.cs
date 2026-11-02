// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// A saved Abro AI chat conversation.
    /// </summary>
    public class AbroAiConversationModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AbroAiConversationModel"/> class.
        /// </summary>
        public AbroAiConversationModel()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.UtcNow;
            ModifiedAt = CreatedAt;
            Title = string.Empty;
            Messages = new List<AbroAiMessageModel>();
        }

        /// <summary>Gets or sets the unique id.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets a short title shown in the conversation list.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets when the conversation was created (UTC).</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Gets or sets when the conversation was last modified (UTC).</summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>Gets or sets the provider used for this conversation, if any.</summary>
        public Guid? ProviderId { get; set; }

        /// <summary>Gets or sets the messages of the conversation.</summary>
        public List<AbroAiMessageModel> Messages { get; set; }

        /// <summary>
        /// Copies all fields onto <paramref name="target"/>.
        /// </summary>
        public void CloneTo(AbroAiConversationModel target)
        {
            target.Id = Id;
            target.Title = Title;
            target.CreatedAt = CreatedAt;
            target.ModifiedAt = ModifiedAt;
            target.ProviderId = ProviderId;
            target.Messages = new List<AbroAiMessageModel>();
            if (Messages == null)
                return;

            foreach (AbroAiMessageModel message in Messages)
            {
                target.Messages.Add(new AbroAiMessageModel
                {
                    Role = message.Role,
                    Content = message.Content,
                    At = message.At,
                });
            }
        }
    }
}
