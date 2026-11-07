// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using Android.Graphics;
using Android.Util;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Shared helpers for saving Voice assist captures into the pinned Voice note.
    /// </summary>
    internal static class VoiceCapturePersist
    {
        private const string LogTag = "aNoteVoice";
        private const int MaxScreenshotEdge = 1200;
        private const int JpegQuality = 72;

        public static bool Save(string recognizedText, string screenshotDataUrl)
        {
            if (string.IsNullOrWhiteSpace(recognizedText) && string.IsNullOrEmpty(screenshotDataUrl))
                return false;

            try
            {
                IRepositoryStorageService repo = Ioc.Instance.GetService<IRepositoryStorageService>();
                if (repo == null)
                {
                    Log.Warn(LogTag, "Repository service unavailable");
                    return false;
                }

                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                NoteModel voiceNote = VoiceNoteHelper.EnsureVoiceNote(model);
                VoiceNoteHelper.PrependCapture(voiceNote, recognizedText, screenshotDataUrl, DateTime.UtcNow);
                bool saved = repo.TrySaveRepository(model);

                try
                {
                    IMessengerService messenger = Ioc.Instance.GetService<IMessengerService>();
                    messenger?.Send(new VoiceNoteChangedMessage());
                }
                catch
                {
                    // Messenger may be unavailable outside the Blazor UI.
                }

                return saved;
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "Save failed: " + ex);
                return false;
            }
        }

        public static string BitmapToJpegDataUrl(Bitmap source)
        {
            if (source == null || source.IsRecycled)
                return null;

            Bitmap scaled = source;
            bool disposeScaled = false;
            try
            {
                int maxEdge = Math.Max(source.Width, source.Height);
                if (maxEdge > MaxScreenshotEdge)
                {
                    float scale = MaxScreenshotEdge / (float)maxEdge;
                    int w = Math.Max(1, (int)(source.Width * scale));
                    int h = Math.Max(1, (int)(source.Height * scale));
                    scaled = Bitmap.CreateScaledBitmap(source, w, h, true);
                    disposeScaled = !ReferenceEquals(scaled, source);
                }

                using var stream = new MemoryStream();
                if (!scaled.Compress(Bitmap.CompressFormat.Jpeg, JpegQuality, stream))
                    return null;

                return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
            }
            finally
            {
                if (disposeScaled && scaled != null && !scaled.IsRecycled)
                    scaled.Recycle();
            }
        }
    }
}
