// Copyright © 2024 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// An accessibility service which draws a small floating button on top of other applications.
    /// The button can be dragged around and opens the application when tapped.
    /// </summary>
    /// <remarks>
    /// The overlay is drawn with the window type <see cref="WindowManagerTypes.AccessibilityOverlay"/>
    /// which is granted together with the accessibility permission, so no SYSTEM_ALERT_WINDOW
    /// permission is required. Android does not allow to enable an accessibility service
    /// programmatically, the user has to enable it in the system settings.
    /// </remarks>
    [Service(
        Exported = true,
        Label = "aNote",
        Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE")]
    [IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
    [MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service_config")]
    public class FloatingButtonAccessibilityService : AccessibilityService
    {
        private const string PreferencesName = "anote_floating_button";
        private const string PreferenceKeyX = "x";
        private const string PreferenceKeyY = "y";
        private const string LogTag = "aNoteFloating";
        private const float TouchSlop = 10f;

        private IWindowManager _windowManager;
        private ImageView _overlayView;
        private WindowManagerLayoutParams _overlayParams;

        /// <summary>
        /// Gets the currently connected instance of the service, or null if the service is not
        /// enabled by the user.
        /// </summary>
        internal static FloatingButtonAccessibilityService Instance { get; private set; }

        /// <inheritdoc/>
        protected override void OnServiceConnected()
        {
            base.OnServiceConnected();
            Instance = this;
            _windowManager = ResolveWindowManager();

            if (ShouldShowFloatingButton())
                ShowOverlay();
        }

        /// <summary>
        /// Obtains the Android window manager from the service context. The returned system service
        /// is only a generic <see cref="Java.Lang.Object"/>, so it must be wrapped explicitly as
        /// <see cref="IWindowManager"/>.
        /// </summary>
        /// <returns>The window manager, or null if it could not be resolved.</returns>
        private IWindowManager ResolveWindowManager()
        {
            try
            {
                Java.Lang.Object systemService = GetSystemService(Context.WindowService);
                if (systemService == null)
                {
                    global::Android.Util.Log.Error(LogTag, "WindowManager service is not available");
                    return null;
                }
                return systemService.JavaCast<IWindowManager>();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Resolving WindowManager failed: " + ex);
                return null;
            }
        }

        /// <inheritdoc/>
        public override void OnAccessibilityEvent(AccessibilityEvent e)
        {
            // The service does not react on accessibility events, it is only used to draw the
            // floating button.
        }

        /// <inheritdoc/>
        public override void OnInterrupt()
        {
        }

        /// <inheritdoc/>
        public override bool OnUnbind(Intent intent)
        {
            HideOverlay();
            Instance = null;
            return base.OnUnbind(intent);
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            HideOverlay();
            Instance = null;
            base.OnDestroy();
        }

        /// <summary>
        /// Adds the floating button to the window manager, if it is not yet shown.
        /// </summary>
        internal void ShowOverlay()
        {
            if (_overlayView != null)
                return;
            if (_windowManager == null)
            {
                global::Android.Util.Log.Error(LogTag, "Cannot show the floating button, the window manager is not available");
                return;
            }

            try
            {
                float density = Resources.DisplayMetrics.Density;
                int size = (int)(48 * density);
                int iconPadding = (int)(12 * density);

                _overlayView = new ImageView(this);
                _overlayView.SetImageResource(Resource.Drawable.floating_button_pencil);
                _overlayView.SetBackgroundResource(Resource.Drawable.floating_button_background);
                _overlayView.SetScaleType(ImageView.ScaleType.Center);
                _overlayView.SetPadding(iconPadding, iconPadding, iconPadding, iconPadding);
                _overlayView.Alpha = 0.7f;

                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                _overlayParams = new WindowManagerLayoutParams(
                    size,
                    size,
                    WindowManagerTypes.AccessibilityOverlay,
                    WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.Start,
                    X = preferences.GetInt(PreferenceKeyX, 0),
                    Y = preferences.GetInt(PreferenceKeyY, (int)(200 * density)),
                };

                _overlayView.SetOnTouchListener(new OverlayTouchListener(this));
                _windowManager.AddView(_overlayView, _overlayParams);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show the floating button: " + ex);
                _overlayView = null;
                _overlayParams = null;
            }
        }

        /// <summary>
        /// Removes the floating button from the window manager, if it is shown.
        /// </summary>
        internal void HideOverlay()
        {
            if (_overlayView == null || _windowManager == null)
                return;

            try
            {
                _windowManager.RemoveView(_overlayView);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not hide the floating button: " + ex);
            }
            _overlayView = null;
            _overlayParams = null;
        }

        /// <summary>
        /// Reads the persisted setting, whether the floating button should be shown. The service can
        /// run independently of the application, so it builds its own service provider.
        /// </summary>
        /// <returns>True if the floating button should be shown.</returns>
        private bool ShouldShowFloatingButton()
        {
            try
            {
                ServiceCollection services = new ServiceCollection();
                MauiProgram.RegisterSharedServices(services);
                MauiProgram.RegisterPlatformServices(services);
                ServiceProvider serviceProvider = services.BuildServiceProvider();
                serviceProvider.GetService<IAppContextService>().InitializeWithContextOnly(this);
                return serviceProvider.GetService<ISettingsService>().LoadSettingsOrDefault().ShowFloatingButton;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not read the settings: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Brings the application to the foreground. Because the service is not an activity, the
        /// intent needs the NewTask flag. The MainActivity uses the SingleTask launch mode, so an
        /// already running instance is reused.
        /// </summary>
        private void OpenApplication()
        {
            Intent intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }

        /// <summary>
        /// Handles dragging of the floating button and opening the application on a tap.
        /// </summary>
        private class OverlayTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private float _touchStartRawX;
            private float _touchStartRawY;
            private int _paramsStartX;
            private int _paramsStartY;
            private bool _isDragging;

            public OverlayTouchListener(FloatingButtonAccessibilityService service)
            {
                _service = service;
            }

            /// <inheritdoc/>
            public bool OnTouch(global::Android.Views.View view, MotionEvent motionEvent)
            {
                switch (motionEvent.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _touchStartRawX = motionEvent.RawX;
                        _touchStartRawY = motionEvent.RawY;
                        _paramsStartX = _service._overlayParams.X;
                        _paramsStartY = _service._overlayParams.Y;
                        _isDragging = false;
                        return true;

                    case MotionEventActions.Move:
                        float dx = motionEvent.RawX - _touchStartRawX;
                        float dy = motionEvent.RawY - _touchStartRawY;
                        if (Math.Abs(dx) > TouchSlop || Math.Abs(dy) > TouchSlop)
                            _isDragging = true;

                        if (_isDragging)
                        {
                            _service._overlayParams.X = _paramsStartX + (int)dx;
                            _service._overlayParams.Y = _paramsStartY + (int)dy;
                            _service._windowManager.UpdateViewLayout(view, _service._overlayParams);
                        }
                        return true;

                    case MotionEventActions.Up:
                        if (_isDragging)
                        {
                            var preferences = _service.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                            preferences.Edit()
                                .PutInt(PreferenceKeyX, _service._overlayParams.X)
                                .PutInt(PreferenceKeyY, _service._overlayParams.Y)
                                .Apply();
                        }
                        else
                        {
                            _service.OpenApplication();
                        }
                        return true;

                    default:
                        return true;
                }
            }
        }
    }
}
