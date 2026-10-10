// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of a single phone contact. A contact consists of a name and a list of
    /// phone numbers.
    /// </summary>
    public class ContactModel
    {
        private Guid _id;
        private List<string> _phones;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContactModel"/> class.
        /// </summary>
        public ContactModel()
        {
            CreatedAt = DateTime.UtcNow;
            ModifiedAt = CreatedAt;
        }

        /// <summary>
        /// Gets or sets the unique id of the contact.
        /// </summary>
        [XmlAttribute("id")]
        public Guid Id
        {
            get { return (_id != Guid.Empty) ? _id : (_id = Guid.NewGuid()); }
            set { _id = value; }
        }

        /// <summary>
        /// Gets or sets the time in UTC, when the contact was created.
        /// </summary>
        [XmlAttribute("created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the time in UTC, when the contact was last modified.
        /// </summary>
        [XmlAttribute("modified_at")]
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Gets or sets the name of the contact.
        /// </summary>
        [XmlElement("name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the phone numbers of the contact.
        /// </summary>
        [XmlArray("phones")]
        [XmlArrayItem("phone")]
        public List<string> Phones
        {
            get { return _phones ?? (_phones = new List<string>()); }
            set { _phones = value; }
        }

        /// <summary>
        /// Sets the <see cref="ModifiedAt"/> property to the current UTC time.
        /// </summary>
        public void RefreshModifiedAt()
        {
            ModifiedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Makes <paramref name="target"/> a deep copy of the contact.
        /// </summary>
        /// <param name="target">Copy all properties to this contact.</param>
        public void CloneTo(ContactModel target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (Object.ReferenceEquals(this, target))
                return;

            target.Id = this.Id;
            target.CreatedAt = this.CreatedAt;
            target.ModifiedAt = this.ModifiedAt;
            target.Name = this.Name;
            target.Phones = new List<string>(this.Phones);
        }

        /// <summary>
        /// Makes a deep copy of the contact.
        /// </summary>
        /// <returns>Copy of the contact.</returns>
        public ContactModel Clone()
        {
            ContactModel result = new ContactModel();
            CloneTo(result);
            return result;
        }
    }
}
