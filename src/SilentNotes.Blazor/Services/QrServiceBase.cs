// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BarcodeScanning;
using SilentNotes.Services;
using ZXing.Net.Maui;
using Color = Microsoft.Maui.Graphics.Color;
using Colors = Microsoft.Maui.Graphics.Colors;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Abstract base implementation of the <see cref="IQrService"/> interface. Live camera scanning
    /// uses native ML Kit via <c>BarcodeScanning.Native.Maui</c> on Android. Image decoding prefers
    /// the native scanner and falls back to ZXing. QR generation remains ZXing-based.
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

            // Prefer native ML Kit / platform decoder when available.
            try
            {
                var nativeResults = await Methods.ScanFromImageAsync(image);
                BarcodeScanning.BarcodeResult native = nativeResults?.FirstOrDefault();
                if (native != null)
                {
                    if (!string.IsNullOrWhiteSpace(native.RawValue))
                        return native.RawValue;
                    if (!string.IsNullOrWhiteSpace(native.DisplayValue))
                        return native.DisplayValue;
                }
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (Exception)
            {
            }

            try
            {
                using (var stream = new MemoryStream(image))
                {
                    var results = await BarcodeReader.DecodeAsync(stream, new ZXing.Net.Maui.BarcodeReaderOptions
                    {
                        Formats = ZXing.Net.Maui.BarcodeFormats.All,
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
