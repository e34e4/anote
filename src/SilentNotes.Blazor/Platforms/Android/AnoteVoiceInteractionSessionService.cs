// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Service.Voice;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Creates a <see cref="AnoteVoiceInteractionSession"/> when the assist gesture fires.
    /// </summary>
    [Register("app.anote.VoiceInteractionSessionService")]
    [Service(
        Name = "app.anote.VoiceInteractionSessionService",
        Exported = true,
        Permission = "android.permission.BIND_VOICE_INTERACTION")]
    public class AnoteVoiceInteractionSessionService : VoiceInteractionSessionService
    {
        /// <inheritdoc/>
        public override VoiceInteractionSession OnNewSession(Bundle args)
        {
            return new AnoteVoiceInteractionSession(this);
        }
    }
}
