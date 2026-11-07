// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Speech;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Stub recognition service required for VoiceInteractionService registration.
    /// Actual dictation uses the system <see cref="SpeechRecognizer"/> inside the session.
    /// </summary>
    [Register("app.anote.AnoteRecognitionService")]
    [Service(
        Name = "app.anote.AnoteRecognitionService",
        Exported = true,
        Permission = "android.permission.BIND_RECOGNITION_SERVICE")]
    [IntentFilter(new[] { "android.speech.RecognitionService" })]
    [MetaData("android.speech", Resource = "@xml/recognition_service")]
    public class AnoteRecognitionService : RecognitionService
    {
        /// <inheritdoc/>
        protected override void OnStartListening(Intent recognizerIntent, Callback listener)
        {
            listener?.Error((int)SpeechRecognizerError.Client);
        }

        /// <inheritdoc/>
        protected override void OnCancel(Callback listener)
        {
        }

        /// <inheritdoc/>
        protected override void OnStopListening(Callback listener)
        {
        }
    }
}
