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
    /// <summary>
    /// Android implementation of <see cref="IAttachmentFileService"/>. Decrypted attachments are
    /// written into the app cache (exposed by the FileProvider) and handed to a viewer app.
    /// </summary>
    internal class AttachmentFileService : IAttachmentFileService
    {
        private const string ShareFolder = "attachments";
        private readonly IAppContextService _appContext;

        public AttachmentFileService(IAppContextService appContext)
        {
            _appContext = appContext;
        }

        /// <inheritdoc/>
        public Task<bool> OpenAttachmentAsync(byte[] content, string fileName, string mimeType)
        {
            return Task.FromResult(OpenOrShare(content, fileName, mimeType, Intent.ActionView));
        }

        /// <inheritdoc/>
        public Task<bool> ShareAttachmentAsync(byte[] content, string fileName, string mimeType)
        {
            return Task.FromResult(OpenOrShare(content, fileName, mimeType, Intent.ActionSend));
        }

        private bool OpenOrShare(byte[] content, string fileName, string mimeType, string action)
        {
            if (content == null || content.Length == 0)
                return false;

            try
            {
                Context context = (Context)_appContext.RootActivity ?? _appContext.Context;
                var directory = new Java.IO.File(context.CacheDir, ShareFolder);
                directory.Mkdirs();

                string safeName = MakeSafeName(fileName);
                var file = new Java.IO.File(directory, DateTime.UtcNow.Ticks + "_" + safeName);
                File.WriteAllBytes(file.AbsolutePath, content);

                global::Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                    context, context.PackageName + ".fileprovider", file);
                var intent = new Intent(action);
                intent.SetDataAndType(uri, string.IsNullOrWhiteSpace(mimeType) ? "*/*" : mimeType);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
                if (action == Intent.ActionSend)
                    intent.PutExtra(Intent.ExtraStream, uri);

                Intent chooser = Intent.CreateChooser(intent, safeName);
                chooser.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(chooser);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string MakeSafeName(string fileName)
        {
            string name = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return "attachment";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }
    }
}
