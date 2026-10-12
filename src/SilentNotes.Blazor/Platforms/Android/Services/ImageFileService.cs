// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.Graphics;
using Android.OS;
using SilentNotes.Services;
using Path = System.IO.Path;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IImageFileService"/> interface for the Android platform.
    /// Images are handed to other apps with a content uri of the app's FileProvider, and saved
    /// pictures are written with the MediaStore so they appear in the gallery.
    /// </summary>
    internal class ImageFileService : IImageFileService
    {
        private const string MimeImage = "image/*";
        private const string ShareFolder = "clipboard";
        private const string PicturesFolderName = "clips";
        private const int JpegQuality = 90;

        private readonly IAppContextService _appContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageFileService"/> class.
        /// </summary>
        public ImageFileService(IAppContextService appContextService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return true; }
        }

        /// <inheritdoc/>
        public Task<bool> OpenImageAsync(string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                        return false;

                    Context context = _appContext.Context;

                    // The FileProvider only shares the "clipboard" folder below the cache directory,
                    // so copy the image there before handing it to another application.
                    var shareDirectory = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDirectory.Mkdirs();
                    string extension = Path.GetExtension(filePath);
                    string shareName = "share_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + extension;
                    var shareFile = new Java.IO.File(shareDirectory, shareName);
                    File.Copy(filePath, shareFile.AbsolutePath, true);

                    global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".fileprovider", shareFile);
                    var intent = new Intent(Intent.ActionView);
                    intent.SetDataAndType(uri, MimeImage);
                    intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                    intent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(intent);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public Task<string> SaveImageToPicturesAsync(string filePath)
        {
            return Task.Run(() =>
            {
                Bitmap bitmap = null;
                try
                {
                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                        return null;

                    bitmap = BitmapFactory.DecodeFile(filePath);
                    if (bitmap == null)
                        return null;

                    string displayName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".jpg";

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    {
                        var values = new ContentValues();
                        values.Put("_display_name", displayName);
                        values.Put("mime_type", "image/jpeg");
                        values.Put("relative_path", "Pictures/" + PicturesFolderName);

                        global::Android.Net.Uri uri = _appContext.Context.ContentResolver.Insert(
                            global::Android.Provider.MediaStore.Images.Media.ExternalContentUri, values);
                        if (uri == null)
                            return null;

                        using (Stream stream = _appContext.Context.ContentResolver.OpenOutputStream(uri))
                        {
                            bitmap.Compress(Bitmap.CompressFormat.Jpeg, JpegQuality, stream);
                        }
                        return "Pictures/" + PicturesFolderName + "/" + displayName;
                    }
                    else
                    {
                        string directory = Path.Combine(
                            global::Android.OS.Environment.GetExternalStoragePublicDirectory("Pictures").AbsolutePath,
                            PicturesFolderName);
                        Directory.CreateDirectory(directory);
                        string targetPath = Path.Combine(directory, displayName);
                        using (FileStream stream = File.Create(targetPath))
                        {
                            bitmap.Compress(Bitmap.CompressFormat.Jpeg, JpegQuality, stream);
                        }
                        return targetPath;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
                finally
                {
                    bitmap?.Recycle();
                }
            });
        }
    }
}
