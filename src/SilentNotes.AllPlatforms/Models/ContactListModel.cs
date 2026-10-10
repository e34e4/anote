// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// List of <see cref="ContactModel"/> objects, used by a <see cref="NoteRepositoryModel"/>.
    /// </summary>
    public class ContactListModel : List<ContactModel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContactListModel"/> class.
        /// </summary>
        public ContactListModel()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContactListModel"/> class.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new list.</param>
        public ContactListModel(IEnumerable<ContactModel> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Searches for a contact with a given id in the list and returns the found contact.
        /// </summary>
        /// <param name="id">Search for the contact with this id.</param>
        /// <returns>Found contact, or null if no such contact exists.</returns>
        public ContactModel FindById(Guid id)
        {
            return Find(item => item.Id == id);
        }

        /// <summary>
        /// Determines whether a contact with a given id is in the list.
        /// </summary>
        /// <param name="id">Search for the contact with this id.</param>
        /// <returns>Returns true if the contact is in the list, otherwise false.</returns>
        public bool ContainsById(Guid id)
        {
            return FindById(id) != null;
        }
    }
}
