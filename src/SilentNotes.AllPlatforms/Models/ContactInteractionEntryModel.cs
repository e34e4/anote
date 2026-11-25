// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// A single local CRM interaction log entry for a contact. Stored only in the encrypted
    /// note repository; never written to Android ContactsContract or other cloud contact providers.
    /// </summary>
    public class ContactInteractionEntryModel
    {
        /// <summary>
        /// Gets or sets the time in UTC when the interaction was recorded.
        /// </summary>
        [XmlAttribute("utc")]
        public DateTime Utc { get; set; }

        /// <summary>
        /// Gets or sets a short summary of the interaction.
        /// </summary>
        [XmlElement("summary")]
        public string Summary { get; set; }
    }
}