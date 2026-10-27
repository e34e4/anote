// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilentNotes.Services;
using ZXing.Net.Maui;
using Color = Microsoft.Maui.Graphics.Color;
using Colors = Microsoft.Maui.Graphics.Colors;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Abstract base implementation of the <see cref="IQrService"/> interface. Decoding images and
    /// generating QR codes is done with the ZXing library and works on all platforms, scanning
    /// with the camera is implemented by the platform specific sub class.
    /// </summary>
    internal abstract class QrServiceBase : IQrService
    {
        /// <inheritdoc/>
        public abstract bool IsScanSupported { get; }

        /// <inheritdoc/>
        public abstract bool HasCameraPermission { get; }

        /// <inheritdoc/>
        public abstract Task<bool> RequestCameraPermissionAsync();

        /// <inheritdoc/>
        public abstract Task<string> ScanQrAsync();

        /// <inheritdoc/>
        public abstract void Vibrate();

        /// <inheritdoc/>
        public async Task<string> DecodeImageAsync(byte[] image)
        {
            if ((image == null) || (image.Length == 0))
                return null;

            try
            {
                using (var stream = new MemoryStream(image))
                {
                    var results = await BarcodeReader.DecodeAsync(stream, new BarcodeReaderOptions
                    {
                        Formats = BarcodeFormats.All,
                        AutoRotate = true,
                        TryHarder = true,
                    });
                    return results?.FirstOrDefault()?.Value;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<byte[]> GenerateQrPngAsync(string text, int size)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            try
            {
                using (var stream = new MemoryStream())
                {
                    await BarcodeGenerator.WriteToStreamAsync(text, stream, new BarcodeGeneratorOptions
                    {
                        Format = BarcodeFormat.QrCode,
                        Width = size,
                        Height = size,
                        Margin = 2,
                        ForegroundColor = Colors.Black,
                        BackgroundColor = Colors.White,
                    },
                    new BarcodeImageOptions
                    {
                        Format = BarcodeImageFormat.Png,
                    });
                    return stream.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public virtual async Task<byte[]> GenerateQrJpegAsync(string text, int size, int quality)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            try
            {
                using (var stream = new MemoryStream())
                {
                    await BarcodeGenerator.WriteToStreamAsync(text, stream, new BarcodeGeneratorOptions
                    {
                        Format = BarcodeFormat.QrCode,
                        Width = size,
                        Height = size,
                        Margin = 2,
                        ForegroundColor = Colors.Black,
                        BackgroundColor = Colors.White,
                    },
                    new BarcodeImageOptions
                    {
                        Format = BarcodeImageFormat.Jpeg,
                    });
                    return stream.ToArray();
                }
            }
            catch (Exception)
            {
                // Fall back to PNG generation when JPEG encoding is unavailable.
                return await GenerateQrPngAsync(text, size);
            }
        }
    }
}
