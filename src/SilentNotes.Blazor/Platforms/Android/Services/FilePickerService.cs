// Copyright © 2021 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using SilentNotes.Services;
using Uri = Android.Net.Uri;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IFilePickerService"/> interface for the Android platform.
    /// </summary>
    internal class FilePickerService : IFilePickerService
    {
        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;
        private Uri _pickedUri;

        /// <summary>
        /// Initializes a new instance of the <see cref="FolderPickerService"/> class.
        /// </summary>
        /// <param name="appContextService">A service which knows about the current main activity.</param>
        /// <param name="activityResultAwaiter">An activity result awaiter.</param>
        public FilePickerService(IAppContextService appContextService, IActivityResultAwaiter activityResultAwaiter)
        {
            _appContext = appContextService;
            _activityResultAwaiter = activityResultAwaiter;
        }

        /// <inheritdoc/>
        public async Task<bool> PickFile(IEnumerable<string> extensions = null, string mimeType = null)
        {
            Intent filePickerIntent = new Intent(Intent.ActionOpenDocument);
            filePickerIntent.AddFlags(ActivityFlags.GrantReadUriPermission);
            filePickerIntent.AddCategory(Intent.CategoryOpenable);
            filePickerIntent.SetType(string.IsNullOrEmpty(mimeType) ? "application/*" : mimeType);

            var activityResult = await _activityResultAwaiter.StartActivityAndWaitForResult(
                _appContext.RootActivity, filePickerIntent);

            if (activityResult.ResultCode == Result.Ok)
            {
                _pickedUri = activityResult.Data?.Data;
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public async Task<bool> PickImage()
        {
            Intent imagePickerIntent = new Intent(Intent.ActionOpenDocument);
            imagePickerIntent.AddFlags(ActivityFlags.GrantReadUriPermission);
            imagePickerIntent.AddCategory(Intent.CategoryOpenable);
            imagePickerIntent.SetType("image/*");

            var activityResult = await _activityResultAwaiter.StartActivityAndWaitForResult(
                _appContext.RootActivity, imagePickerIntent);

            if (activityResult.ResultCode == Result.Ok)
            {
                _pickedUri = activityResult.Data?.Data;
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public async Task<byte[]> ReadPickedFile()
        {
            if (_pickedUri == null)
                throw new Exception("Pick a file first before it can be read.");

            // Read the stream sequentially instead of using its Length, because providers like
            // Google Photos return a stream which does not support the Length property.
            using (Stream stream = _appContext.RootActivity.ContentResolver.OpenInputStream(_pickedUri))
            {
                if (stream == null)
                    throw new Exception("The picked file could not be opened.");

                using (var memory = new MemoryStream())
                {
                    await stream.CopyToAsync(memory);
                    return memory.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public string GetPickedFileName()
        {
            if (_pickedUri == null)
                return null;

            try
            {
                using (var cursor = _appContext.RootActivity.ContentResolver.Query(
                    _pickedUri, new[] { global::Android.Provider.IOpenableColumns.DisplayName }, null, null, null))
                {
                    if ((cursor != null) && cursor.MoveToFirst())
                    {
                        int nameIndex = cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.DisplayName);
                        if (nameIndex >= 0)
                        {
                            string name = cursor.GetString(nameIndex);
                            if (!string.IsNullOrWhiteSpace(name))
                                return name;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            string lastSegment = _pickedUri.LastPathSegment;
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                int slash = lastSegment.LastIndexOf('/');
                int colon = lastSegment.LastIndexOf(':');
                int sep = Math.Max(slash, colon);
                return (sep >= 0 && sep + 1 < lastSegment.Length) ? lastSegment.Substring(sep + 1) : lastSegment;
            }
            return "file.bin";
        }
    }
}