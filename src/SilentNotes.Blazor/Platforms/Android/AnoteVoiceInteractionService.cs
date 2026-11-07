// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Service.Voice;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Marks aNote as a digital assistant candidate for <c>ROLE_ASSISTANT</c>.
    /// </summary>
    [Register("app.anote.VoiceInteractionService")]
    [Service(
        Name = "app.anote.VoiceInteractionService",
        Exported = true,
        Permission = "android.permission.BIND_VOICE_INTERACTION")]
    [IntentFilter(new[] { "android.service.voice.VoiceInteractionService" })]
    [MetaData("android.voice_interaction", Resource = "@xml/voice_interaction_service")]
    public class AnoteVoiceInteractionService : VoiceInteractionService
    {
    }
}
