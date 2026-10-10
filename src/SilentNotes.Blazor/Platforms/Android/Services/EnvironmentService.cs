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
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="IEnvironmentService"/> for the Android platform.
    /// </summary>
    internal class EnvironmentService : IEnvironmentService, IKeepScreenOn, IScreenshots, IFloatingButtonService, IClipboardMonitorService, IBrowserUrlProvider
    {
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
            get { return FloatingButtonAccessibilityService.Instance?.IsClipboardCaptureActive ?? false; }
        }

        /// <inheritdoc/>
        bool IClipboardMonitorService.IsAccessibilityServiceEnabled
        {
            get
            {
                if (FloatingButtonAccessibilityService.Instance != null)
                    return true;

                try
                {
                    string enabled = global::Android.Provider.Settings.Secure.GetString(
                        _appContext.Context.ContentResolver,
                        global::Android.Provider.Settings.Secure.EnabledAccessibilityServices);
                    if (string.IsNullOrEmpty(enabled))
                        return false;

                    string componentId = new ComponentName(
                        _appContext.Context,
                        Java.Lang.Class.FromType(typeof(FloatingButtonAccessibilityService))).FlattenToString();
                    return enabled.Contains(componentId);
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.SetMonitoringEnabled(bool enabled)
        {
            FloatingButtonAccessibilityService.Instance?.SetClipboardMonitoringEnabled(enabled);
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.RequestAccessibilityPermission()
        {
            ((IClipboardMonitorService)this).OpenAccessibilitySettings();
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.OpenAccessibilitySettings()
        {
            try
            {
                var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
                intent.AddFlags(ActivityFlags.NewTask);
                _appContext.Context.StartActivity(intent);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("*** Could not open the accessibility settings: " + ex.Message);
            }
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.RefreshSettings()
        {
            try
            {
                SettingsModel settings = Ioc.Instance.GetService<ISettingsService>()?.LoadSettingsOrDefault();
                if (settings != null)
                    FloatingButtonAccessibilityService.Instance?.SetClipboardMonitoringEnabled(settings.ClipboardMonitoringEnabled);
            }
            catch (System.Exception)
            {
            }
        }

        /// <inheritdoc/>
        void IClipboardMonitorService.CaptureNow()
        {
            try
            {
                SettingsModel settings = Ioc.Instance.GetService<ISettingsService>()?.LoadSettingsOrDefault();
                if (settings?.ClipboardMonitoringEnabled != true)
                    return;

                // The application has the focus at this moment, so it is allowed to read the clipboard.
                new ClipboardCapture(_appContext.Context, Ioc.Instance).CaptureCurrentClip();
            }
            catch (System.Exception)
            {
            }
        }
    }
}
