// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Root model of the encrypted password vault file (plaintext while unlocked).
    /// </summary>
    [XmlRoot("password_vault")]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public class PasswordVaultModel
    {
        private List<PasswordEntryModel> _entries;

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordVaultModel"/> class.
        /// </summary>
        public PasswordVaultModel()
        {
            CreatedAt = DateTime.UtcNow;
            ModifiedAt = CreatedAt;
        }

        /// <summary>Gets or sets when the vault was created (UTC).</summary>
        [XmlAttribute("created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>Gets or sets when the vault was last modified (UTC).</summary>
        [XmlAttribute("modified_at")]
        public DateTime ModifiedAt { get; set; }

        /// <summary>Gets or sets the login entries.</summary>
        [XmlArray("entries")]
        [XmlArrayItem("entry")]
        public List<PasswordEntryModel> Entries
        {
            get { return _entries ?? (_entries = new List<PasswordEntryModel>()); }
            set { _entries = value; }
        }

        /// <summary>Finds an entry by id.</summary>
        public PasswordEntryModel FindById(Guid id)
        {
            return Entries.FirstOrDefault(e => e.Id == id);
        }

        /// <summary>Marks the vault as modified now.</summary>
        public void RefreshModifiedAt()
        {
            ModifiedAt = DateTime.UtcNow;
        }
    }
}
