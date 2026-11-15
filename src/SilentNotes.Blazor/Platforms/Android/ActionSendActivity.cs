// Copyright © 2021 Martin Stoeckli / aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using SilentNotes.Platforms;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Handles content which is shared from another application (ACTION_SEND). Shared text can be
    /// routed to a note, a bookmark, an RSS subscription or the clipboard; shared images can be
    /// saved to the clipboard, the Camera tab, a note or scanned with OCR; shared files can be
    /// opened in the Reader.
    /// </summary>
    [Activity(Label = "aNote", NoHistory = true, Exported = true, Theme = "@android:style/Theme.Translucent.NoTitleBar")]
    [IntentFilter(
        new[] { Intent.ActionSend },
        Categories = new[] { Intent.CategoryDefault },
        DataMimeType = @"text/plain")]
    [IntentFilter(
        new[] { Intent.ActionSend },
        Categories = new[] { Intent.CategoryDefault },
        DataMimeType = @"image/*")]
    [IntentFilter(
        new[] { Intent.ActionSend },
        Categories = new[] { Intent.CategoryDefault },
        DataMimeType = @"application/*")]
    public class ActionSendActivity : Activity
    {
        /// <summary>Intent parameter which carries the shared text to the main activity.</summary>
        public const string SharedTextParam = "ActionSendSharedText";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            string sharedText = Intent?.GetStringExtra(Intent.ExtraText);
            global::Android.Net.Uri stream = null;
            try
            {
                stream = (global::Android.Net.Uri)Intent?.GetParcelableExtra(Intent.ExtraStream);
            }
            catch (Exception)
            {
            }

            string mimeType = Intent?.Type ?? string.Empty;
            if (stream != null && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                ShowImageChooser(stream, mimeType, sharedText);
            else if (stream != null)
                ShowFileChooser(stream, mimeType, sharedText);
            else
                ShowTextChooser(sharedText);
        }

        private void ShowTextChooser(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Finish();
                return;
            }

            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("aNote");
            builder.SetItems(
                new[] { "New note", "Bookmark", "RSS feed", "Clipboard" },
                (sender, args) =>
                {
                    switch (args.Which)
                    {
                        case 0:
                            ShareTargetActions.CreateNote(this, text);
                            break;
                        case 1:
                            ShareTargetActions.CreateBookmark(this, text);
                            break;
                        case 2:
                            _ = ShareTargetActions.ProbeRssFeedAsync(this, text);
                            break;
                        default:
                            ShareTargetActions.SaveTextToClipboard(this, text);
                            break;
                    }
                    Finish();
                });
            builder.SetNegativeButton("Cancel", (sender, args) => Finish());
            builder.SetOnDismissListener(new DismissListener(Finish));
            builder.Show();
        }

        private void ShowImageChooser(global::Android.Net.Uri stream, string mimeType, string sharedText)
        {
            byte[] bytes = ShareTargetActions.ReadAllBytes(this, stream);
            if (bytes == null || bytes.Length == 0)
            {
                Finish();
                return;
            }

            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("aNote image");
            builder.SetItems(
                new[] { "Clipboard history", "Camera tab", "Note attachment", "OCR text" },
                (sender, args) =>
                {
                    switch (args.Which)
                    {
                        case 0:
                            ShareTargetActions.SaveImageToClipboard(this, bytes);
                            break;
                        case 1:
                            _ = ShareTargetActions.SaveImageToCameraAsync(this, bytes);
                            break;
                        case 2:
                            ShareTargetActions.CreateImageNote(this, bytes);
                            break;
                        default:
                            _ = ShareTargetActions.RecognizeTextAsync(this, bytes);
                            break;
                    }
                    Finish();
                });
            builder.SetNegativeButton("Cancel", (sender, args) => Finish());
            builder.SetOnDismissListener(new DismissListener(Finish));
            builder.Show();
        }

        private void ShowFileChooser(global::Android.Net.Uri stream, string mimeType, string sharedText)
        {
            byte[] bytes = ShareTargetActions.ReadAllBytes(this, stream);
            if (bytes == null || bytes.Length == 0)
            {
                Finish();
                return;
            }

            string fileName = ShareTargetActions.GetDisplayName(this, stream, "shared_file");
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("aNote file");
            builder.SetItems(
                new[] { "Open in Reader", "New note with link", "Clipboard (text)" },
                (sender, args) =>
                {
                    switch (args.Which)
                    {
                        case 0:
                            string path = ShareTargetActions.SaveFileToReader(this, bytes, fileName, mimeType);
                            if (!string.IsNullOrEmpty(path))
                                OpenReader(path, mimeType);
                            break;
                        case 1:
                            string text = string.IsNullOrWhiteSpace(sharedText)
                                ? fileName + "\n" + mimeType
                                : sharedText;
                            ShareTargetActions.CreateNote(this, text);
                            break;
                        default:
                            ShareTargetActions.SaveTextToClipboard(
                                this,
                                string.IsNullOrWhiteSpace(sharedText) ? fileName : sharedText);
                            break;
                    }
                    Finish();
                });
            builder.SetNegativeButton("Cancel", (sender, args) => Finish());
            builder.SetOnDismissListener(new DismissListener(Finish));
            builder.Show();
        }

        private void OpenReader(string path, string mimeType)
        {
            Intent mainIntent = new Intent(this, typeof(MainActivity));
            mainIntent.PutExtra(FloatingButtonAccessibilityService.ExtraOpenTab, 15);
            mainIntent.PutExtra(ShareTargetActions.ExtraOpenDocumentPath, path);
            mainIntent.PutExtra(ShareTargetActions.ExtraOpenDocumentMime, mimeType);
            StartActivity(mainIntent);
        }

        private sealed class DismissListener : Java.Lang.Object, IDialogInterfaceOnDismissListener
        {
            private readonly Action _dismiss;

            public DismissListener(Action dismiss)
            {
                _dismiss = dismiss;
            }

            public void OnDismiss(IDialogInterface dialog)
            {
                _dismiss?.Invoke();
            }
        }
    }
}
