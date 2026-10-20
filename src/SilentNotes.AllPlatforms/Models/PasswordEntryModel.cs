// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// A single login entry in the password vault (Bitwarden-like).
    /// </summary>
    public class PasswordEntryModel
    {
        private Guid _id;
        private List<string> _uris;

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordEntryModel"/> class.
        /// </summary>
        public PasswordEntryModel()
        {
            CreatedAt = DateTime.UtcNow;
            ModifiedAt = CreatedAt;
            TotpPeriod = 30;
            TotpDigits = 6;
        }

        /// <summary>Gets or sets the unique id of the entry.</summary>
        [XmlAttribute("id")]
        public Guid Id
        {
            get { return (_id != Guid.Empty) ? _id : (_id = Guid.NewGuid()); }
            set { _id = value; }
        }

        /// <summary>Gets or sets when the entry was created (UTC).</summary>
        [XmlAttribute("created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>Gets or sets when the entry was last modified (UTC).</summary>
        [XmlAttribute("modified_at")]
        public DateTime ModifiedAt { get; set; }

        /// <summary>Gets or sets the display name.</summary>
        [XmlElement("name")]
        public string Name { get; set; }

        /// <summary>Gets or sets the username / email.</summary>
        [XmlElement("username")]
        public string Username { get; set; }

        /// <summary>Gets or sets the password.</summary>
        [XmlElement("password")]
        public string Password { get; set; }

        /// <summary>Gets or sets associated URIs / package ids.</summary>
        [XmlArray("uris")]
        [XmlArrayItem("uri")]
        public List<string> Uris
        {
            get { return _uris ?? (_uris = new List<string>()); }
            set { _uris = value; }
        }

        /// <summary>Gets or sets free-form notes.</summary>
        [XmlElement("notes")]
        public string Notes { get; set; }

        /// <summary>Gets or sets the TOTP secret (raw Base32 or otpauth URI).</summary>
        [XmlElement("totp_secret")]
        public string TotpSecret { get; set; }

        /// <summary>Gets or sets the TOTP period in seconds (default 30).</summary>
        [XmlAttribute("totp_period")]
        public int TotpPeriod { get; set; }

        /// <summary>Gets or sets the TOTP digit count (default 6).</summary>
        [XmlAttribute("totp_digits")]
        public int TotpDigits { get; set; }

        /// <summary>Marks the entry as modified now.</summary>
        public void RefreshModifiedAt()
        {
            ModifiedAt = DateTime.UtcNow;
        }

        /// <summary>Creates a deep copy of the entry.</summary>
        public PasswordEntryModel Clone()
        {
            var result = new PasswordEntryModel
            {
                Id = Id,
                CreatedAt = CreatedAt,
                ModifiedAt = ModifiedAt,
                Name = Name,
                Username = Username,
                Password = Password,
                Notes = Notes,
                TotpSecret = TotpSecret,
                TotpPeriod = TotpPeriod,
                TotpDigits = TotpDigits,
            };
            result.Uris.AddRange(Uris);
            return result;
        }
    }
}
