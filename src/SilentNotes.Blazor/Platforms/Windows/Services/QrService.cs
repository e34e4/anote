// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

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
