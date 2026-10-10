// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.OS;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// This activity handles shared text (ACTION_SEND) and creates a bookmark draft from it. A
    /// shared page url becomes the bookmark url, the remaining text becomes the quote and the
    /// subject becomes the title.
    /// </summary>
    [Activity(Label = "aNote bookmark", NoHistory = true, Exported = true)]
    [IntentFilter(
        new[] { Intent.ActionSend },
        Categories = new[] { Intent.CategoryDefault },
        DataMimeType = @"text/plain")]
    public class BookySendActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if (Intent?.Action != Intent.ActionSend)
            {
                Finish();
                return;
            }

            string shared = Intent.GetStringExtra(Intent.ExtraText) ?? string.Empty;
            string subject = Intent.GetStringExtra(Intent.ExtraSubject) ?? string.Empty;

            string url = BookyUrlUtils.FirstUrl(shared);
            string quote = (url != null) ? shared.Replace(url, string.Empty).Trim() : shared.Trim();

            // Use the clipboard as a quote fallback when the shared text was only a url.
            if (string.IsNullOrEmpty(quote))
            {
                string clipboardText = GetClipboardText();
                if (!BookyUrlUtils.LooksLikeUrl(clipboardText))
                    quote = clipboardText;
            }

            Intent mainActivityIntent = new Intent(this, typeof(MainActivity));
            if (!string.IsNullOrEmpty(url))
                mainActivityIntent.PutExtra(ProcessTextActivity.UrlParam, url);
            if (!string.IsNullOrEmpty(subject))
                mainActivityIntent.PutExtra(ProcessTextActivity.TitleParam, subject);
            if (!string.IsNullOrEmpty(quote))
                mainActivityIntent.PutExtra(ProcessTextActivity.QuoteParam, quote);
            StartActivity(mainActivityIntent);
            Finish();
        }

        private string GetClipboardText()
        {
            var clipboardManager = GetSystemService(Context.ClipboardService) as ClipboardManager;
            ClipData clip = clipboardManager?.PrimaryClip;
            if ((clip == null) || (clip.ItemCount == 0))
                return null;
            return clip.GetItemAt(0)?.CoerceToText(this)?.ToString();
        }
    }
}
