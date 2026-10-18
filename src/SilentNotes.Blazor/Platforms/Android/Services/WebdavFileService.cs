// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using Java.Net;
using SilentNotes.Services;
using Path = System.IO.Path;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IWebdavFileService"/>.
    /// </summary>
    internal class WebdavFileService : WebdavFileServiceBase
    {
        private const string ShareFolder = "clipboard";
        private const string DownloadsSubFolder = "aNote";

        private readonly IAppContextService _appContext;

        public WebdavFileService(IAppContextService appContextService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        public override Task<string> SaveDownloadedFileAsync(string fileName, byte[] content)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(fileName) || content == null)
                        return null;

                    string safeName = Path.GetFileName(fileName);
                    string mimeType = URLConnection.GuessContentTypeFromName(safeName) ?? "application/octet-stream";
                    Context context = _appContext.Context;

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    {
                        var values = new ContentValues();
                        values.Put("_display_name", safeName);
                        values.Put("mime_type", mimeType);
                        values.Put("relative_path", "Download/" + DownloadsSubFolder);

                        global::Android.Net.Uri uri = context.ContentResolver.Insert(
                            global::Android.Provider.MediaStore.Downloads.ExternalContentUri, values);
                        if (uri == null)
                            return null;

                        using (Stream output = context.ContentResolver.OpenOutputStream(uri))
                        {
                            output.Write(content, 0, content.Length);
                        }
                        return "Download/" + DownloadsSubFolder + "/" + safeName;
                    }
                    else
                    {
                        string dir = Path.Combine(
                            global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                                global::Android.OS.Environment.DirectoryDownloads).AbsolutePath,
                            DownloadsSubFolder);
                        Directory.CreateDirectory(dir);
                        string targetPath = Path.Combine(dir, safeName);
                        File.WriteAllBytes(targetPath, content);
                        return targetPath;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public override Task<bool> ShareDownloadedFileAsync(string fileName, byte[] content)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(fileName) || content == null)
                        return false;

                    string safeName = Path.GetFileName(fileName);
                    string mimeType = URLConnection.GuessContentTypeFromName(safeName) ?? "*/*";
                    Context context = _appContext.Context;

                    var shareDir = new Java.IO.File(context.CacheDir, ShareFolder);
                    shareDir.Mkdirs();
                    var shareFile = new Java.IO.File(shareDir, safeName);
                    File.WriteAllBytes(shareFile.AbsolutePath, content);

                    global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                        context, context.PackageName + ".fileprovider", shareFile);

                    var intent = new Intent(Intent.ActionSend);
                    intent.SetType(mimeType);
                    intent.PutExtra(Intent.ExtraStream, uri);
                    intent.AddFlags(ActivityFlags.GrantReadUriPermission);

                    Intent chooser = Intent.CreateChooser(intent, safeName);
                    chooser.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(chooser);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }
    }
}
