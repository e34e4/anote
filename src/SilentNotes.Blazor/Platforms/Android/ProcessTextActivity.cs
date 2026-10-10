// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.OS;

namespace SilentNotes
{
    /// <summary>
    /// This activity handles the PROCESS_TEXT action, which is triggered when the user selects text
    /// in another application and chooses "aNote" from the text selection menu. It stores the
    /// selected text in the clipboard history of aNote.
    /// </summary>
    [Activity(Label = "aNote", NoHistory = true, Exported = true)]
    [IntentFilter(
        new[] { Intent.ActionProcessText },
        Categories = new[] { Intent.CategoryDefault },
        DataMimeType = @"text/plain")]
    public class ProcessTextActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            string selectedText = Intent.GetCharSequenceExtra(Intent.ExtraProcessText)?.ToString()?.Trim();

            // Let the main activity store the text in the clipboard history, it has all services
            // initialized. The NoHistory flag avoids that this activity is opened again later.
            Intent mainActivityIntent = new Intent(this, typeof(MainActivity));
            mainActivityIntent.PutExtra(ActionSendActivity.SharedTextParam, selectedText);
            StartActivity(mainActivityIntent);

            Finish();
        }
    }
}
