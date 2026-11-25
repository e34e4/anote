// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// Serializeable model of a single phone contact. A contact consists of a name and a list of
    /// phone numbers, plus optional local-only CRM fields (notes, follow-up schedule, interaction log).
    /// </summary>
    /// <remarks>
    /// CRM fields are stored only in the encrypted aNote repository. They are not synced to
    /// Android ContactsContract or other system/cloud contact providers.
    /// </remarks>
    public class ContactModel
    {
        private Guid _id;
        private List<string> _phones;
        private List<ContactInteractionEntryModel> _interactionLog;
        private DateTime? _lastInteractionUtc;
        private DateTime? _nextFollowUpUtc;

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
        /// Local-only free-form notes about this contact (encrypted repository only).
        /// </summary>
        [XmlElement("notes")]
        public string Notes { get; set; }

        public bool NotesSpecified { get { return !string.IsNullOrEmpty(Notes); } }

        /// <summary>
        /// Days between follow-ups; 0 means no follow-up schedule.
        /// </summary>
        [XmlAttribute("follow_up_days")]
        public int FollowUpIntervalDays { get; set; }

        public bool FollowUpIntervalDaysSpecified { get { return FollowUpIntervalDays != 0; } }

        /// <summary>
        /// Time in UTC when this contact was last marked as contacted.
        /// </summary>
        [XmlIgnore]
        public DateTime? LastInteractionUtc
        {
            get { return _lastInteractionUtc; }
            set { _lastInteractionUtc = value; }
        }

        [XmlAttribute("last_interaction_utc")]
        public DateTime LastInteractionUtcSerializeable
        {
            get { return LastInteractionUtc.Value; }
            set { LastInteractionUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
        }

        public bool LastInteractionUtcSerializeableSpecified { get { return LastInteractionUtc != null; } }

        /// <summary>
        /// Next follow-up due time in UTC; computed when the interval changes or an interaction is logged.
        /// </summary>
        [XmlIgnore]
        public DateTime? NextFollowUpUtc
        {
            get { return _nextFollowUpUtc; }
            set { _nextFollowUpUtc = value; }
        }

        [XmlAttribute("next_follow_up_utc")]
        public DateTime NextFollowUpUtcSerializeable
        {
            get { return NextFollowUpUtc.Value; }
            set { NextFollowUpUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
        }

        public bool NextFollowUpUtcSerializeableSpecified { get { return NextFollowUpUtc != null; } }

        /// <summary>
        /// Local interaction history for this contact (encrypted repository only).
        /// </summary>
        [XmlArray("interaction_log")]
        [XmlArrayItem("interaction")]
        public List<ContactInteractionEntryModel> InteractionLog
        {
            get { return _interactionLog ?? (_interactionLog = new List<ContactInteractionEntryModel>()); }
            set { _interactionLog = value; }
        }

        public bool InteractionLogSpecified { get { return InteractionLog.Count > 0; } }

        /// <summary>
        /// Returns whether a follow-up is due on or before today (UTC date).
        /// </summary>
        public static bool IsFollowUpDue(DateTime? nextFollowUpUtc)
        {
            if (!nextFollowUpUtc.HasValue)
                return false;
            return nextFollowUpUtc.Value.Date <= DateTime.UtcNow.Date;
        }

        /// <summary>
        /// Recomputes <see cref="NextFollowUpUtc"/> from <see cref="FollowUpIntervalDays"/> and
        /// <see cref="LastInteractionUtc"/> (or the current UTC time when there is no prior interaction).
        /// </summary>
        public void RefreshFollowUpSchedule()
        {
            if (FollowUpIntervalDays <= 0)
            {
                NextFollowUpUtc = null;
                return;
            }

            DateTime baseTime = LastInteractionUtc ?? DateTime.UtcNow;
            NextFollowUpUtc = baseTime.AddDays(FollowUpIntervalDays);
        }

        /// <summary>
        /// Records an interaction, updates follow-up dates, and appends a log entry.
        /// </summary>
        /// <param name="summary">Short description; use null or empty for a generic contacted entry.</param>
        public void RecordInteraction(string summary)
        {
            DateTime now = DateTime.UtcNow;
            LastInteractionUtc = now;
            if (FollowUpIntervalDays > 0)
                NextFollowUpUtc = now.AddDays(FollowUpIntervalDays);
            else
                NextFollowUpUtc = null;

            InteractionLog.Add(new ContactInteractionEntryModel
            {
                Utc = now,
                Summary = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim(),
            });
            RefreshModifiedAt();
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
            target.Notes = this.Notes;
            target.FollowUpIntervalDays = this.FollowUpIntervalDays;
            target.LastInteractionUtc = this.LastInteractionUtc;
            target.NextFollowUpUtc = this.NextFollowUpUtc;
            target.InteractionLog = this.InteractionLog
                .Select(entry => new ContactInteractionEntryModel { Utc = entry.Utc, Summary = entry.Summary })
                .ToList();
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
