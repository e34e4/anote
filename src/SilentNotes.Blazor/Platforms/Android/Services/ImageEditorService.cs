// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <inheritdoc/>
    internal sealed class ImageEditorService : IImageEditorService
    {
        private readonly IAppContextService _appContext;

        public ImageEditorService(IAppContextService appContext)
        {
            _appContext = appContext;
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public Task<bool> OpenEditorAsync(string filePath)
        {
            try
            {
                string path = filePath;
                if (string.IsNullOrWhiteSpace(path))
                    return Task.FromResult(false);

                if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    && Uri.TryCreate(path, UriKind.Absolute, out Uri uri))
                {
                    path = uri.LocalPath;
                }

                if (!File.Exists(path))
                    return Task.FromResult(false);

                Context context = _appContext.Context;
                Intent intent = ImageEditorActivity.CreateIntent(context, path);
                if (context is not global::Android.App.Activity)
                    intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
    }
}
