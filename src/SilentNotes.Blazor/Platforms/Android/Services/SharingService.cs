// Copyright © 2025 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using Android.Content;
using Android.Text;
using SilentNotes.Services;
using Uri = Android.Net.Uri;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="ISharingService"/> interface for the Android platform.
    /// </summary>
    internal class SharingService : ISharingService
    {
        private readonly IAppContextService _appContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="ISharingService"/> class.
        /// </summary>
        /// <param name="appContextService">A service which knows about the current main activity.</param>
        public SharingService(IAppContextService appContextService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        public Task ShareHtmlText(string htmlText, string plainText, string subject)
        {
            Intent shareIntent = new Intent(Intent.ActionSend);
            shareIntent.SetType("text/html");
            shareIntent.PutExtra(Intent.ExtraSubject, subject);
            shareIntent.PutExtra(Intent.ExtraText, plainText);
            shareIntent.PutExtra(Intent.ExtraHtmlText, htmlText);

            // Following statement may work for some receiving apps, but in an email client, tags
            // like lists are removed completely:
            // shareIntent.PutExtra(Intent.ExtraText, Html.FromHtml(htmlText, FromHtmlOptions.ModeLegacy));

            Intent chooserIntent = Intent.CreateChooser(shareIntent, "Share");
            chooserIntent.AddFlags(ActivityFlags.NewTask);
            _appContext.RootActivity.StartActivity(chooserIntent);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ShareText(string text)
        {
            Intent shareIntent = new Intent(Intent.ActionSend);
            shareIntent.SetType("text/plain");
            shareIntent.PutExtra(Intent.ExtraText, text);

            Intent chooserIntent = Intent.CreateChooser(shareIntent, "Share");
            chooserIntent.AddFlags(ActivityFlags.NewTask);
            _appContext.RootActivity.StartActivity(chooserIntent);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ShareFile(string filePath, string mimeType = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return Task.CompletedTask;

            try
            {
                Context context = _appContext.RootActivity ?? _appContext.Context;
                if (context == null)
                    return Task.CompletedTask;

                string safeName = Path.GetFileName(filePath);
                string mime = mimeType;
                if (string.IsNullOrWhiteSpace(mime))
                {
                    mime = string.Equals(Path.GetExtension(filePath), ".log", StringComparison.OrdinalIgnoreCase)
                        ? "text/plain"
                        : "*/*";
                }

                // Copy into cache so FileProvider always has a stable path under the app.
                var shareDir = new Java.IO.File(context.CacheDir, "log_share");
                shareDir.Mkdirs();
                var shareFile = new Java.IO.File(shareDir, safeName);
                File.Copy(filePath, shareFile.AbsolutePath, overwrite: true);

                Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                    context, context.PackageName + ".fileprovider", shareFile);

                Intent shareIntent = new Intent(Intent.ActionSend);
                shareIntent.SetType(mime);
                shareIntent.PutExtra(Intent.ExtraStream, uri);
                shareIntent.PutExtra(Intent.ExtraSubject, safeName);
                shareIntent.AddFlags(ActivityFlags.GrantReadUriPermission);

                Intent chooserIntent = Intent.CreateChooser(shareIntent, safeName);
                chooserIntent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(chooserIntent);
            }
            catch (Exception)
            {
            }

            return Task.CompletedTask;
        }
    }
}
