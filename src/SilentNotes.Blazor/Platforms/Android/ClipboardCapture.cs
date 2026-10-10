// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.Graphics;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Reads the system clipboard and stores its content in the clipboard history.
    /// </summary>
    /// <remarks>
    /// Android only allows the app which currently has focus to read the clipboard, with the
    /// exception of a running accessibility service. Therefore this capture is used by the
    /// <see cref="FloatingButtonAccessibilityService"/>, which can read the clipboard without
    /// stealing the focus (and without making the keyboard disappear).
    /// </remarks>
    internal class ClipboardCapture
    {
        private const string SensitiveExtraKey = "android.content.extra.IS_SENSITIVE";
        private const int MaxFullImageDimension = 1200;
        private const int MaxThumbnailDimension = 200;
        private const string LogTag = "aNoteClipboard";

        private readonly Context _context;
        private readonly IServiceProvider _serviceProvider;
        private string _lastSignature;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipboardCapture"/> class.
        /// </summary>
        /// <param name="context">A context to access the clipboard service and the content resolver.</param>
        /// <param name="serviceProvider">A service provider with the clipboard history service.</param>
        public ClipboardCapture(Context context, IServiceProvider serviceProvider)
        {
            _context = context;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Reads the current clipboard content and stores it, if it changed since the last call.
        /// </summary>
        public void CaptureCurrentClip()
        {
            try
            {
                var clipboardManager = _context.GetSystemService(Context.ClipboardService) as ClipboardManager;
                if ((clipboardManager == null) || !clipboardManager.HasPrimaryClip)
                    return;

                ClipData clip = clipboardManager.PrimaryClip;
                if ((clip == null) || (clip.ItemCount == 0))
                    return;

                // Ignore clips which the source app marked as sensitive (e.g. passwords).
                if ((clip.Description?.Extras != null) && clip.Description.Extras.GetBoolean(SensitiveExtraKey, false))
                    return;

                ClipData.Item item = clip.GetItemAt(0);
                if (item == null)
                    return;

                string signature;
                if (item.Text != null)
                    signature = "t:" + item.Text;
                else if (item.Uri != null)
                    signature = "u:" + item.Uri;
                else
                    return;

                if (signature == _lastSignature)
                    return;
                _lastSignature = signature;

                if (item.Text != null)
                    StoreText(item.Text);
                else
                    StoreImage(item.Uri);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not read the clipboard: " + ex);
            }
        }

        /// <summary>
        /// Stores a given text in the clipboard history, e.g. a text which was selected in another
        /// app and captured with a gesture.
        /// </summary>
        /// <param name="text">The text to store.</param>
        public void StoreText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            int limit = GetHistorySizeLimit();
            Task.Run(() =>
            {
                _serviceProvider?.GetService<IClipboardHistoryService>()?.AddText(text, limit);
                NotifyClipboardChanged();
            });
        }

        private void StoreImage(global::Android.Net.Uri uri)
        {
            Task.Run(() =>
            {
                try
                {
                    byte[] raw;
                    using (Stream stream = _context.ContentResolver.OpenInputStream(uri))
                    {
                        if (stream == null)
                            return;
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            raw = memory.ToArray();
                        }
                    }

                    byte[] fullImage = null;
                    byte[] thumbnail = null;
                    using (Bitmap decoded = DecodeDownscaled(raw, MaxFullImageDimension))
                    {
                        if (decoded == null)
                            return;
                        fullImage = CompressPng(decoded);
                        using (Bitmap thumb = CreateScaledBitmap(decoded, MaxThumbnailDimension))
                            thumbnail = CompressPng(thumb);
                    }

                    _serviceProvider?.GetService<IClipboardHistoryService>()?.AddImage(fullImage, thumbnail, GetHistorySizeLimit());
                    NotifyClipboardChanged();
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error(LogTag, "Could not store an image clip: " + ex);
                }
            });
        }

        private int GetHistorySizeLimit()
        {
            try
            {
                SettingsModel settings = _serviceProvider?.GetService<ISettingsService>()?.LoadSettingsOrDefault();
                return ((settings != null) && (settings.ClipboardHistorySize >= 0)) ? settings.ClipboardHistorySize : 500;
            }
            catch (Exception)
            {
                return 500;
            }
        }

        private void NotifyClipboardChanged()
        {
            try
            {
                _serviceProvider?.GetService<IMessengerService>()?.Send(new ClipboardChangedMessage());
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not notify about a clipboard change: " + ex);
            }
        }

        private static Bitmap DecodeDownscaled(byte[] raw, int maxDimension)
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeByteArray(raw, 0, raw.Length, bounds);
            if ((bounds.OutWidth <= 0) || (bounds.OutHeight <= 0))
                return null;

            int sampleSize = 1;
            int largest = Math.Max(bounds.OutWidth, bounds.OutHeight);
            while (largest / sampleSize > maxDimension)
                sampleSize *= 2;

            var options = new BitmapFactory.Options { InSampleSize = sampleSize };
            return BitmapFactory.DecodeByteArray(raw, 0, raw.Length, options);
        }

        private static Bitmap CreateScaledBitmap(Bitmap source, int maxDimension)
        {
            int width = source.Width;
            int height = source.Height;
            float scale = Math.Min(1f, (float)maxDimension / Math.Max(width, height));
            int targetWidth = Math.Max(1, (int)(width * scale));
            int targetHeight = Math.Max(1, (int)(height * scale));
            return Bitmap.CreateScaledBitmap(source, targetWidth, targetHeight, true);
        }

        private static byte[] CompressPng(Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Compress(Bitmap.CompressFormat.Png, 100, memory);
                return memory.ToArray();
            }
        }
    }
}
