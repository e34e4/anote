// Copyright © 2021 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.OS;

namespace SilentNotes
{
    /// <summary>
    /// This activity handles text which is shared from another application (ACTION_SEND) and stores
    /// it in the clipboard history of aNote.
    /// </summary>
    [Activity(Label = "aNote", NoHistory = true, Exported = true)]
    [IntentFilter(
        new[] { Intent.ActionSend },
        Categories = new[] { Intent.CategoryDefault },
        DataMimeType = @"text/plain")]
    public class ActionSendActivity : Activity
    {
        /// <summary>Intent parameter which carries the shared text to the main activity.</summary>
        public const string SharedTextParam = "ActionSendSharedText";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            string sharedText = Intent?.GetStringExtra(Intent.ExtraText);

            // Let the main activity store the text in the clipboard history, it has all services
            // initialized. The NoHistory flag avoids that this activity is opened again later.
            Intent mainActivityIntent = new Intent(this, typeof(MainActivity));
            mainActivityIntent.PutExtra(SharedTextParam, sharedText);
            StartActivity(mainActivityIntent);

            Finish();
        }
    }
}
