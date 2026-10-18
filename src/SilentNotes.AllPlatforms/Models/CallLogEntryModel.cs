// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Models
{
    /// <summary>
    /// Enumeration of call log entry directions/types.
    /// </summary>
    public enum CallDirection
    {
        Incoming = 1,
        Outgoing = 2,
        Missed = 3,
        Rejected = 5,
    }

    /// <summary>
    /// Represents a single entry from the system call log.
    /// </summary>
    public class CallLogEntryModel
    {
        /// <summary>Gets or sets the database id of the call log row.</summary>
        public long Id { get; set; }

        /// <summary>Gets or sets the phone number.</summary>
        public string Number { get; set; }

        /// <summary>Gets or sets the resolved contact name, if any.</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the call direction/type.</summary>
        public CallDirection Direction { get; set; }

        /// <summary>Gets or sets the UTC time when the call started.</summary>
        public DateTime DateUtc { get; set; }

        /// <summary>Gets or sets the call duration in seconds.</summary>
        public long DurationSeconds { get; set; }
    }

    /// <summary>
    /// Represents an ongoing or incoming phone call handled by the InCallService.
    /// </summary>
    public class ActiveCallModel
    {
        /// <summary>Gets or sets the remote phone number.</summary>
        public string Number { get; set; }

        /// <summary>Gets or sets the resolved contact name, if any.</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the call state label (e.g. Ringing, Dialing, Active, Disconnected).</summary>
        public string State { get; set; }

        /// <summary>Gets or sets a value indicating whether the call is ringing (incoming) and can be answered.</summary>
        public bool IsRinging { get; set; }

        /// <summary>Gets or sets a value indicating whether the call is currently active/connected.</summary>
        public bool IsActive { get; set; }

        /// <summary>Gets or sets a value indicating whether the microphone is muted.</summary>
        public bool IsMuted { get; set; }

        /// <summary>Gets or sets a value indicating whether the speakerphone is enabled.</summary>
        public bool IsSpeakerOn { get; set; }

        /// <summary>Gets or sets the UTC time when the call became active, if connected.</summary>
        public DateTime? ConnectedAtUtc { get; set; }

        /// <summary>Gets or sets the caller ID / spam summary from the spam database or AON lookup.</summary>
        public string SpamSummary { get; set; }

        /// <summary>Gets or sets a value indicating whether the caller is flagged as spam.</summary>
        public bool IsSpam { get; set; }
    }
}
