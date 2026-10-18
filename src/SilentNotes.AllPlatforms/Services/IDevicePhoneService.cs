// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Gives the Phone tab access to call history, placing phone calls, active call management,
    /// and setting aNote as the system default phone (dialer) app.
    /// </summary>
    public interface IDevicePhoneService
    {
        /// <summary>Gets a value indicating whether the platform supports telephony/call log.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether call log and phone call permissions are granted.</summary>
        bool HasPermissions { get; }

        /// <summary>Requests call log and phone permissions from the user.</summary>
        Task<bool> RequestPermissionsAsync();

        /// <summary>Gets a value indicating whether aNote is currently the system default phone app.</summary>
        bool IsDefaultDialerApp { get; }

        /// <summary>Asks the user to make aNote the system default phone app.</summary>
        Task<bool> RequestDefaultDialerAppAsync();

        /// <summary>Gets the recent call history entries, newest first.</summary>
        Task<IReadOnlyList<CallLogEntryModel>> GetCallLogAsync();

        /// <summary>Deletes a single entry from the call history.</summary>
        Task DeleteCallLogEntryAsync(long id);

        /// <summary>Clears the entire call history.</summary>
        Task ClearCallLogAsync();

        /// <summary>Places a phone call to the specified number.</summary>
        Task<bool> PlaceCallAsync(string number);

        /// <summary>Gets information about the current incoming or ongoing phone call, or null if none.</summary>
        ActiveCallModel GetActiveCall();

        /// <summary>Answers an incoming ringing phone call.</summary>
        void AnswerCall();

        /// <summary>Hangs up or rejects the current phone call.</summary>
        void HangupCall();

        /// <summary>Toggles microphone mute during an active call.</summary>
        void ToggleMute();

        /// <summary>Toggles speakerphone during an active call.</summary>
        void ToggleSpeaker();

        /// <summary>Sends a DTMF keypad tone during an active call.</summary>
        void SendDtmf(char digit);
    }
}
