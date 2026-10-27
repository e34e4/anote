// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IQrService"/> interface for the Windows platform.
    /// Scanning with the camera is not supported, but QR codes can be decoded from image files
    /// and generated.
    /// </summary>
    internal class QrService : QrServiceBase
    {
        /// <inheritdoc/>
        public override async Task<byte[]> GenerateQrJpegAsync(string text, int size, int quality)
        {
            byte[] png = await GenerateQrPngAsync(text, size);
            if (png == null || png.Length == 0)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    using (var input = new MemoryStream(png))
                    using (var bitmap = new Bitmap(input))
                    using (var output = new MemoryStream())
                    {
                        ImageCodecInfo jpegCodec = Array.Find(
                            ImageCodecInfo.GetImageEncoders(),
                            codec => codec.FormatID == ImageFormat.Jpeg.Guid);
                        if (jpegCodec == null)
                        {
                            bitmap.Save(output, ImageFormat.Jpeg);
                            return output.ToArray();
                        }

                        using (var encoderParams = new EncoderParameters(1))
                        {
                            long clamped = Math.Clamp(quality, 1, 100);
                            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, clamped);
                            bitmap.Save(output, jpegCodec, encoderParams);
                        }
                        return output.ToArray();
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public override bool IsScanSupported
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public override bool HasCameraPermission
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public override Task<bool> RequestCameraPermissionAsync()
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public override Task<string> ScanQrAsync()
        {
            return Task.FromResult<string>(null);
        }

        /// <inheritdoc/>
        public override void Vibrate()
        {
        }
    }
}
