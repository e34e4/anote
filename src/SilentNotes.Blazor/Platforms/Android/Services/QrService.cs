// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.Content.PM;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IQrService"/> interface for the Android platform. The
    /// camera scanning shows a full screen page with the camera preview, which is closed as soon
    /// as a code was found or the user cancels.
    /// </summary>
    internal class QrService : QrServiceBase
    {
        private readonly IAppContextService _appContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="QrService"/> class.
        /// </summary>
        /// <param name="appContextService">A service which knows about the current main activity.</param>
        public QrService(IAppContextService appContextService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        public override bool IsScanSupported
        {
            get { return true; }
        }

        /// <inheritdoc/>
        public override bool HasCameraPermission
        {
            get
            {
                try
                {
                    return ContextCompat.CheckSelfPermission(_appContext.Context, global::Android.Manifest.Permission.Camera) == Permission.Granted;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public override async Task<bool> RequestCameraPermissionAsync()
        {
            try
            {
                if (HasCameraPermission)
                    return true;
                PermissionStatus status = await Permissions.RequestAsync<Permissions.Camera>();
                return status == PermissionStatus.Granted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public override async Task<string> ScanQrAsync()
        {
            try
            {
                var resultSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                Page previousPage = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Window window = Application.Current.Windows[0];
                    Page oldPage = window.Page;
                    window.Page = new QrScanPage(resultSource);
                    return oldPage;
                });

                string result = await resultSource.Task;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Application.Current.Windows[0].Page = previousPage;
                });
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public override void Vibrate()
        {
            try
            {
                if (Vibration.Default.IsSupported)
                    Vibration.Default.Vibrate(200);
            }
            catch (Exception)
            {
            }
        }
    }
}
