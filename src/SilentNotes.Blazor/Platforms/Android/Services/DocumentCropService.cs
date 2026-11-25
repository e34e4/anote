// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using SilentNotes;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <inheritdoc/>
    internal sealed class DocumentCropService : IDocumentCropService
    {
        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;

        public DocumentCropService(IAppContextService appContext, IActivityResultAwaiter activityResultAwaiter)
        {
            _appContext = appContext;
            _activityResultAwaiter = activityResultAwaiter;
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public async Task<byte[]> CropDocumentAsync(string filePath)
        {
            string path = filePath;
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(path, UriKind.Absolute, out Uri uri))
            {
                path = uri.LocalPath;
            }

            if (!File.Exists(path))
                return null;

            Activity activity = _appContext.RootActivity;
            if (activity == null)
                return null;

            Intent intent = DocumentCropActivity.CreateIntent(activity, path);
            ActivityResult result = await _activityResultAwaiter.StartActivityAndWaitForResult(activity, intent);
            if (result.ResultCode != Result.Ok || result.Data == null)
                return null;

            string resultPath = result.Data.GetStringExtra(DocumentCropActivity.ExtraResultPath);
            if (string.IsNullOrWhiteSpace(resultPath) || !File.Exists(resultPath))
                return null;

            try
            {
                return await File.ReadAllBytesAsync(resultPath);
            }
            finally
            {
                try
                {
                    File.Delete(resultPath);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}