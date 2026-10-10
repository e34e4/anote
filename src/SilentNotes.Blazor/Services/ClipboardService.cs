// Copyright © 2023 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using SilentNotes.Services;

#if ANDROID
using System.IO;
using Android.Content;
#endif

namespace SilentNotes.Services
{
    /// <summary>
    /// Impelementation of the <see cref="IClipboardService"/> interface.
    /// </summary>
    internal class ClipboardService : IClipboardService
    {
        /// <inheritdoc/>
        public async Task SetTextAsync(string text)
        {
            await Clipboard.SetTextAsync(text);
        }

        /// <inheritdoc/>
        public async Task SetImageAsync(byte[] png)
        {
#if ANDROID
            if ((png == null) || (png.Length == 0))
                return;

            await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() =>
            {
                Context context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
                string directory = Path.Combine(FileSystem.CacheDirectory, "clipboard");
                Directory.CreateDirectory(directory);
                string filePath = Path.Combine(directory, "clipboard_image.png");
                File.WriteAllBytes(filePath, png);

                var javaFile = new Java.IO.File(filePath);
                global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                    context, context.PackageName + ".fileprovider", javaFile);

                var clipboardManager = context.GetSystemService(Context.ClipboardService) as ClipboardManager;
                if (clipboardManager == null)
                    return;

                ClipData clip = ClipData.NewUri(context.ContentResolver, "aNote", uri);
                clipboardManager.PrimaryClip = clip;
            });
#else
            await Task.CompletedTask;
#endif
        }
    }
}
