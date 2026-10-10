// Copyright © 2020 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Debug = System.Diagnostics.Debug;
using AndroidX.Core.Content;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IEnvironmentService"/> for the Android platform.
    /// </summary>
    internal class EnvironmentService : IEnvironmentService, IKeepScreenOn, IScreenshots, IFloatingButtonService, IClipboardMonitorService, IBrowserUrlProvider
    {
        private const int NotificationPermissionRequestCode = 0x0A17;

        private readonly IAppContextService _appContext;
        private readonly IMessengerService _messengerService;
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="EnvironmentService"/> class.
        /// </summary>
        /// <param name="appContextService">A service which knows about the current main activity.</param>
        /// <param name="messengerService">The messenger service.</param>
        public EnvironmentService(IAppContextService appContextService, IMessengerService messengerService)
        {
            Os = SilentNotes.Services.OperatingSystem.Android;
            _appContext = appContextService;
            _messengerService = messengerService;
        }

        /// <inheritdoc/>
        public SilentNotes.Services.OperatingSystem Os { get; private set; }

        /// <inheritdoc/>
        public bool InDarkMode
        {
            get
            {
                UiMode nightModeFlags = _appContext.Context.Resources.Configuration.UiMode & UiMode.NightMask;
                return nightModeFlags == UiMode.NightYes;
            }
        }

        /// <inheritdoc/>
        public IKeepScreenOn KeepScreenOn
        {
            get { return this; }
        }

        /// <inheritdoc/>
        void IKeepScreenOn.Start(System.TimeSpan duration)
        {
            Debug.WriteLine("*** IKeepScreenOn.Start");

            // If a timer is already active, deactivate it.
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = null;

            // Set the activity flag to keep the screen on.
            _appContext.RootActivity.Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            // (Re)start the timer
            _cancellationTokenSource = new CancellationTokenSource();
            Task.Delay(duration, _cancellationTokenSource.Token).ContinueWith(StoppedAfterTimeout);
        }

        private void StoppedAfterTimeout(Task task)
        {
            Debug.WriteLine(string.Format("*** IKeepScreenOn.StoppedAfterTimeout WasCanceled={0}", task.IsCanceled));
            if (task.IsCanceled)
                return;

            // Timer was not cancelled, so timeout was reached and KeepScreenOn should be stopped.
            _cancellationTokenSource = null;
            _appContext.RootActivity.RunOnUiThread(() =>
            {
                _appContext.RootActivity.Window.ClearFlags(WindowManagerFlags.KeepScreenOn); // must be called on UI thread
                _messengerService.Send(new KeepScreenOnChangedMessage());
            });
        }

        /// <inheritdoc/>
        void IKeepScreenOn.Stop()
        {
            Debug.WriteLine("*** IKeepScreenOn.Stop");
            if (_cancellationTokenSource != null)
            {
                // If a timer is active, deactivate it.
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = null;

                _appContext.RootActivity.RunOnUiThread(() =>
                {
                    _appContext.RootActivity.Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
                });
            }
        }

        /// <inheritdoc/>
        bool IKeepScreenOn.IsActive
        {
            get { return _cancellationTokenSource != null; }
        }

        /// <inheritdoc/>
        public IScreenshots Screenshots
        {
            get { return this; }
        }

        /// <inheritdoc/>
        bool IScreenshots.PreventScreenshots
        {
            set
            {
                if (value)
                    _appContext.RootActivity.Window.AddFlags(WindowManagerFlags.Secure);
                else
                    _appContext.RootActivity.Window.ClearFlags(WindowManagerFlags.Secure);
            }
        }

        /// <inheritdoc/>
        public IFloatingButtonService FloatingButton
        {
            get { return this; }
        }

        /// <inheritdoc/>
        void IFloatingButtonService.SetFloatingButtonVisible(bool visible)
        {
            if (visible)
                FloatingButtonAccessibilityService.Instance?.ShowOverlay();
            else
                FloatingButtonAccessibilityService.Instance?.HideOverlay();
        }

        /// <inheritdoc/>
        void IFloatingButtonService.OpenAccessibilitySettings()
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
            intent.AddFlags(ActivityFlags.NewTask);
            _appContext.Context.StartActivity(intent);
        }

        /// <inheritdoc/>
        public IBrowserUrlProvider BrowserUrl
        {
            get { return this; }
        }

        /// <inheritdoc/>
        string IBrowserUrlProvider.LastBrowserUrl()
        {
            return BrowserUrlTracker.CaptureEnabled ? BrowserUrlTracker.LastBrowserUrl() : null;
        }

        /// <inheritdoc/>
        bool IBrowserUrlProvider.IsCaptureActive
        {
            get { return BrowserUrlTracker.ServiceEnabled && BrowserUrlTracker.CaptureEnabled; }
        }

        /// <inheritdoc/>
        void IBrowserUrlProvider.SetCaptureEnabled(bool enabled)
        {
            BrowserUrlTracker.CaptureEnabled = enabled;
            FloatingButtonAccessibilityService.Instance?.SetCaptureEnabled(enabled);
        }

        /// <inheritdoc/>
        public IClipboardMonitorService ClipboardMonitor
        {
            get { return this; }
        }

        /// <inheritdoc/>
        bool IClipboardMonitorService.CanMonitor
        {
            get { return true; }
        }

        /// <inheritdoc/>
        bool IClipboardMonitorService.IsMonitoringActive
        {
            get { return ClipboardMonitorService.IsRunning; }
        }

        /// <inheritdoc/>
        bool IClipboardMonitorService.HasOverlayPermission
        {
            get { return global::Android.Provider.Settings.CanDrawOverlays(_appContext.Context); }
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.SetMonitoringEnabled(bool enabled)
        {
            if (enabled)
            {
                RequestNotificationPermission();
                var intent = new Intent(_appContext.Context, typeof(ClipboardMonitorService));
                intent.SetAction(ClipboardMonitorService.ActionStart);
                ContextCompat.StartForegroundService(_appContext.Context, intent);
            }
            else
            {
                var intent = new Intent(_appContext.Context, typeof(ClipboardMonitorService));
                _appContext.Context.StopService(intent);
            }
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.RequestOverlayPermission()
        {
            if (global::Android.Provider.Settings.CanDrawOverlays(_appContext.Context))
                return;

            if (!TryGrantOverlayPermissionWithRoot())
                ((IClipboardMonitorService)this).OpenOverlayPermissionSettings();
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.OpenOverlayPermissionSettings()
        {
            try
            {
                var intent = new Intent(global::Android.Provider.Settings.ActionManageOverlayPermission);
                intent.SetData(global::Android.Net.Uri.Parse("package:" + _appContext.Context.PackageName));
                intent.AddFlags(ActivityFlags.NewTask);
                _appContext.Context.StartActivity(intent);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*** Could not open the overlay permission settings: " + ex.Message);
            }
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.RefreshSettings()
        {
            if (ClipboardMonitorService.IsRunning)
            {
                var intent = new Intent(_appContext.Context, typeof(ClipboardMonitorService));
                intent.SetAction(ClipboardMonitorService.ActionStart);
                ContextCompat.StartForegroundService(_appContext.Context, intent);
            }
        }

        private bool TryGrantOverlayPermissionWithRoot()
        {
            try
            {
                string command = "appops set " + _appContext.Context.PackageName + " SYSTEM_ALERT_WINDOW allow";
                Java.Lang.Process process = Java.Lang.Runtime.GetRuntime().Exec(new[] { "su", "-c", command });
                process.WaitFor();
                return process.ExitValue() == 0;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private void RequestNotificationPermission()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
                return;

            global::Android.App.Activity activity = _appContext.RootActivity;
            if (activity == null)
                return;

            if (ContextCompat.CheckSelfPermission(activity, global::Android.Manifest.Permission.PostNotifications) == global::Android.Content.PM.Permission.Granted)
                return;

            activity.RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, NotificationPermissionRequestCode);
        }
    }
}
