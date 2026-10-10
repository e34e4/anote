// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.App;
using Android.Content;
using Android.OS;
using SilentNotes.Platforms;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// This activity handles the PROCESS_TEXT action, which is triggered when the user selects text
    /// in another application (e.g. a browser) and chooses "Bookmark" from the text selection menu.
    /// It collects the selected text and the current browser url and starts the main activity, which
    /// opens the add-bookmark dialog.
    /// </summary>
    [Activity(Label = "aNote bookmark", NoHistory = true, Exported = true)]
    [IntentFilter(
        new[] { Intent.ActionProcessText },
        Categories = new[] { Intent.CategoryDefault },
        DataMimeType = @"text/plain")]
    public class ProcessTextActivity : Activity
    {
        /// <summary>Intent parameter carrying the url of the page.</summary>
        public const string UrlParam = "BookyUrl";

        /// <summary>Intent parameter carrying the title of the page.</summary>
        public const string TitleParam = "BookyTitle";

        /// <summary>Intent parameter carrying the selected text quote.</summary>
        public const string QuoteParam = "BookyQuote";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            string quote = Intent.GetCharSequenceExtra(Intent.ExtraProcessText)?.ToString()?.Trim();
            if (string.IsNullOrEmpty(quote))
            {
                Finish();
                return;
            }

            // The browser url is not passed along with the selection, so take it from the
            // accessibility service, and fall back to a url found on the clipboard.
            string url = BrowserUrlTracker.LastBrowserUrl();
            if (string.IsNullOrEmpty(url))
                url = BookyUrlUtils.FirstUrl(GetClipboardText());

            StartBookmarkDraft(url, null, quote);
        }

        private void StartBookmarkDraft(string url, string title, string quote)
        {
            Intent mainActivityIntent = new Intent(this, typeof(MainActivity));
            if (!string.IsNullOrEmpty(url))
                mainActivityIntent.PutExtra(UrlParam, url);
            if (!string.IsNullOrEmpty(title))
                mainActivityIntent.PutExtra(TitleParam, title);
            if (!string.IsNullOrEmpty(quote))
                mainActivityIntent.PutExtra(QuoteParam, quote);
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
