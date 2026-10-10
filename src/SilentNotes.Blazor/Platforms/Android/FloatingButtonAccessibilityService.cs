// Copyright © 2024 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using SilentNotes.Workers;

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
        private const int LongPressDelayMs = 600;
        private const int SelectionMaxAgeSeconds = 120;

        private static readonly string[] BrowserPackages = new[]
        {
            "com.brave.browser",
            "com.brave.browser_beta",
            "com.brave.browser_nightly",
            "com.android.chrome",
            "com.chrome.beta",
            "com.chrome.dev",
            "com.microsoft.emmx",
            "com.opera.browser",
            "com.vivaldi.browser",
            "org.mozilla.firefox",
            "org.mozilla.firefox_beta",
        };

        private static readonly string[] UrlViewIds = new[]
        {
            "url_bar",
            "url_bar_title",
            "search_box_text",
            "omnibox_url_bar",
            "mozac_browser_toolbar_url_view",
            "toolbar_url_bar",
        };

        private IWindowManager _windowManager;
        private ImageView _overlayView;
        private WindowManagerLayoutParams _overlayParams;
        private bool _captureEnabled;

        private IServiceProvider _serviceProvider;
        private ClipboardCapture _clipboardCapture;
        private ClipboardManager _clipboardManager;
        private ClipboardChangedListener _clipboardListener;
        private Handler _gestureHandler;
        private string _lastSelectionText;
        private DateTime _lastSelectionTime;

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

            _captureEnabled = ShouldCaptureBrowserUrl();
            BrowserUrlTracker.ServiceEnabled = true;
            BrowserUrlTracker.CaptureEnabled = _captureEnabled;

            if (ShouldShowFloatingButton())
                ShowOverlay();

            if (_captureEnabled)
                RefreshFromActiveWindow();

            _gestureHandler = new Handler(Looper.MainLooper);
            RefreshClipboardSettings();
        }

        /// <summary>
        /// Gets a value indicating whether the clipboard is currently being captured.
        /// </summary>
        internal bool IsClipboardCaptureActive
        {
            get { return _clipboardListener != null; }
        }

        /// <summary>
        /// Enables or disables the capture of the browser url. Called when the user changes the
        /// setting while the service is already connected.
        /// </summary>
        /// <param name="enabled">True to capture the browser url.</param>
        internal void SetCaptureEnabled(bool enabled)
        {
            _captureEnabled = enabled;
            BrowserUrlTracker.CaptureEnabled = enabled;
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
            if (e == null)
                return;

            // Remember the last text selection, because many applications (e.g. web pages) do not
            // expose the selection through their accessibility nodes.
            RememberTextSelection(e);

            // Besides drawing the floating button, the service can remember the url of the page
            // which is currently open in a supported browser, so it can be used as the default url
            // when a bookmark is created.
            if (!_captureEnabled)
                return;

            string packageName = e.PackageName?.ToString();
            if (string.IsNullOrEmpty(packageName) || !BrowserPackages.Contains(packageName))
                return;

            RefreshFromPackage(packageName);
        }

        /// <summary>
        /// Remembers the text which was selected last, using the accessibility selection events.
        /// </summary>
        /// <remarks>
        /// The selection is deliberately not cleared when windows change, because showing the
        /// selection toolbar itself already produces such events, which would discard the selection
        /// before the user can long press the floating button.
        /// </remarks>
        private void RememberTextSelection(AccessibilityEvent e)
        {
            try
            {
                if (e.EventType != EventTypes.ViewTextSelectionChanged)
                    return;

                string text = null;
                if ((e.Text != null) && (e.Text.Count > 0))
                    text = e.Text[0]?.ToString();
                if (string.IsNullOrEmpty(text) && (e.Source != null))
                    text = e.Source.Text?.ToString();

                int from = e.FromIndex;
                int to = e.ToIndex;
                if (string.IsNullOrEmpty(text) || (from < 0) || (to > text.Length) || (to <= from))
                    return;

                _lastSelectionText = text.Substring(from, to - from);
                _lastSelectionTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not remember the text selection: " + ex);
            }
        }

        /// <summary>
        /// Gets the text which was selected most recently, or null if there was no recent selection.
        /// </summary>
        private string GetRecentSelection()
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
                return null;
            if ((DateTime.UtcNow - _lastSelectionTime).TotalSeconds > SelectionMaxAgeSeconds)
                return null;
            return _lastSelectionText;
        }

        /// <inheritdoc/>
        public override void OnInterrupt()
        {
        }

        /// <inheritdoc/>
        public override bool OnUnbind(Intent intent)
        {
            StopClipboardCapture();
            HideOverlay();
            Instance = null;
            BrowserUrlTracker.ServiceEnabled = false;
            BrowserUrlTracker.CaptureEnabled = false;
            return base.OnUnbind(intent);
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            StopClipboardCapture();
            HideOverlay();
            Instance = null;
            BrowserUrlTracker.ServiceEnabled = false;
            BrowserUrlTracker.CaptureEnabled = false;
            base.OnDestroy();
        }

        /// <summary>
        /// Starts or stops the clipboard capture, according to the persisted setting. Called when
        /// the service connects.
        /// </summary>
        private void RefreshClipboardSettings()
        {
            if (ShouldMonitorClipboard())
                StartClipboardCapture();
            else
                StopClipboardCapture();
        }

        /// <summary>
        /// Enables or disables the clipboard capture. Called by the application when the user
        /// changes the setting, because the service keeps its own copy of the settings.
        /// </summary>
        /// <param name="enabled">True to capture clipboard changes, false to stop capturing.</param>
        internal void SetClipboardMonitoringEnabled(bool enabled)
        {
            if (enabled)
                StartClipboardCapture();
            else
                StopClipboardCapture();
        }

        private void StartClipboardCapture()
        {
            if (_clipboardListener != null)
                return;

            try
            {
                _clipboardManager = GetSystemService(Context.ClipboardService) as ClipboardManager;
                if (_clipboardManager == null)
                    return;

                _clipboardCapture = new ClipboardCapture(this, GetServiceProvider());
                _clipboardListener = new ClipboardChangedListener(() => _clipboardCapture?.CaptureCurrentClip());
                _clipboardManager.AddPrimaryClipChangedListener(_clipboardListener);

                // Read the current clip once, so the history is seeded immediately.
                _clipboardCapture.CaptureCurrentClip();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not start the clipboard capture: " + ex);
                _clipboardListener = null;
            }
        }

        private void StopClipboardCapture()
        {
            try
            {
                if ((_clipboardManager != null) && (_clipboardListener != null))
                    _clipboardManager.RemovePrimaryClipChangedListener(_clipboardListener);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not stop the clipboard capture: " + ex);
            }
            _clipboardListener = null;
        }

        /// <summary>
        /// Captures the text which is currently selected in the focused field of the active
        /// application and stores it in the clipboard history. This is triggered by a long press on
        /// the floating button.
        /// </summary>
        internal void CaptureSelectedText()
        {
            string text = ReadSelectedText();
            if (string.IsNullOrWhiteSpace(text))
                text = GetRecentSelection();

            ILanguageService language = GetServiceProvider()?.GetService<ILanguageService>();

            if (string.IsNullOrWhiteSpace(text))
            {
                Toast.MakeText(this, language?["clipboard_gesture_none"] ?? "No text selected", ToastLength.Short)?.Show();
                return;
            }

            try
            {
                _clipboardCapture = _clipboardCapture ?? new ClipboardCapture(this, GetServiceProvider());
                _clipboardCapture.StoreText(text);
                Toast.MakeText(this, language?["clipboard_gesture_saved"] ?? "Text saved to the clipboard history", ToastLength.Short)?.Show();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not capture the selected text: " + ex);
            }
        }

        /// <summary>
        /// Reads the text which is currently selected in the active application. The text and its
        /// selection are read from the accessibility tree, so the clipboard is not involved and the
        /// other application keeps the focus.
        /// </summary>
        /// <returns>The selected text, or null if no text is selected.</returns>
        private string ReadSelectedText()
        {
            try
            {
                AccessibilityNodeInfo root = RootInActiveWindow;
                if (root == null)
                    return null;

                AccessibilityNodeInfo node = FindNodeWithSelection(root, 0);
                if (node == null)
                    return null;

                string text = node.Text?.ToString();
                int start = node.TextSelectionStart;
                int end = node.TextSelectionEnd;
                if (string.IsNullOrEmpty(text) || (start < 0) || (end > text.Length) || (end <= start))
                    return null;

                return text.Substring(start, end - start);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not read the selected text: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Searches the accessibility tree for the node which contains a text selection.
        /// </summary>
        private static AccessibilityNodeInfo FindNodeWithSelection(AccessibilityNodeInfo node, int depth)
        {
            if ((node == null) || (depth > 30))
                return null;

            int start = node.TextSelectionStart;
            int end = node.TextSelectionEnd;
            if ((start >= 0) && (end > start) && (node.Text != null))
                return node;

            for (int index = 0; index < node.ChildCount; index++)
            {
                AccessibilityNodeInfo found = FindNodeWithSelection(node.GetChild(index), depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Listener which is notified by the clipboard manager whenever the clipboard changes.
        /// </summary>
        private class ClipboardChangedListener : Java.Lang.Object, ClipboardManager.IOnPrimaryClipChangedListener
        {
            private readonly System.Action _callback;

            public ClipboardChangedListener(System.Action callback)
            {
                _callback = callback;
            }

            public void OnPrimaryClipChanged()
            {
                _callback();
            }
        }

        private void RefreshFromActiveWindow()
        {
            string packageName = RootInActiveWindow?.PackageName?.ToString();
            if (!string.IsNullOrEmpty(packageName) && BrowserPackages.Contains(packageName))
                RefreshFromPackage(packageName);
        }

        private void RefreshFromPackage(string packageName)
        {
            var roots = new List<AccessibilityNodeInfo>();
            AccessibilityNodeInfo activeRoot = RootInActiveWindow;
            if (activeRoot != null)
                roots.Add(activeRoot);

            IList<AccessibilityWindowInfo> windows = Windows;
            if (windows != null)
            {
                foreach (AccessibilityWindowInfo window in windows)
                {
                    if (window?.Root != null)
                        roots.Add(window.Root);
                }
            }

            foreach (AccessibilityNodeInfo root in roots)
            {
                string url = FindUrl(root, packageName);
                if (!string.IsNullOrEmpty(url))
                {
                    BrowserUrlTracker.Update(url, packageName);
                    return;
                }
            }
        }

        private string FindUrl(AccessibilityNodeInfo root, string packageName)
        {
            foreach (string viewId in UrlViewIds)
            {
                IList<AccessibilityNodeInfo> nodes = root.FindAccessibilityNodeInfosByViewId(packageName + ":id/" + viewId);
                if (nodes == null)
                    continue;
                foreach (AccessibilityNodeInfo node in nodes)
                {
                    string url = ExtractUrl(node);
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }
            }
            return WalkForUrl(root, 0);
        }

        private static string ExtractUrl(AccessibilityNodeInfo node)
        {
            var candidates = new List<string>();
            if (node.Text != null)
                candidates.Add(node.Text.ToString());
            if (node.ContentDescription != null)
                candidates.Add(node.ContentDescription.ToString());
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
            {
                string hint = node.HintText?.ToString();
                if (!string.IsNullOrEmpty(hint))
                    candidates.Add(hint);
            }

            foreach (string candidate in candidates)
            {
                string normalized = NormalizeOmnibox(candidate);
                string url = BookyUrlUtils.FirstUrl(normalized);
                if (!string.IsNullOrEmpty(url))
                    return url;

                // The omnibox sometimes shows the host without a scheme while browsing https.
                if (LooksLikeHostPath(normalized))
                    return "https://" + normalized;
            }
            return null;
        }

        private static string WalkForUrl(AccessibilityNodeInfo node, int depth)
        {
            if ((node == null) || (depth > 12))
                return null;

            string url = ExtractUrl(node);
            if (!string.IsNullOrEmpty(url))
                return url;

            for (int index = 0; index < node.ChildCount; index++)
            {
                AccessibilityNodeInfo child = node.GetChild(index);
                if (child == null)
                    continue;
                string childUrl = WalkForUrl(child, depth + 1);
                if (!string.IsNullOrEmpty(childUrl))
                    return childUrl;
            }
            return null;
        }

        private static string NormalizeOmnibox(string raw)
        {
            string result = (raw ?? string.Empty).Trim();
            if (result.StartsWith("Secure", StringComparison.Ordinal))
                result = result.Substring("Secure".Length).Trim();
            if (result.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && !result.Contains("://"))
                result = "https://" + result;
            return result;
        }

        private static bool LooksLikeHostPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains(' ') || value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!value.Contains('.'))
                return false;

            try
            {
                System.Uri uri = new System.Uri("https://" + value);
                return !string.IsNullOrEmpty(uri.Host);
            }
            catch (Exception)
            {
                return false;
            }
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
        /// Reads the persisted setting, whether the floating button should be shown.
        /// </summary>
        /// <returns>True if the floating button should be shown.</returns>
        private bool ShouldShowFloatingButton()
        {
            return LoadSettings()?.ShowFloatingButton ?? false;
        }

        /// <summary>
        /// Reads the persisted setting, whether the browser url should be captured.
        /// </summary>
        /// <returns>True if the browser url should be captured.</returns>
        private bool ShouldCaptureBrowserUrl()
        {
            return LoadSettings()?.BookyUrlCaptureEnabled ?? false;
        }

        /// <summary>
        /// Reads the persisted setting, whether the clipboard should be monitored.
        /// </summary>
        /// <returns>True if the clipboard should be monitored.</returns>
        private bool ShouldMonitorClipboard()
        {
            return LoadSettings()?.ClipboardMonitoringEnabled ?? false;
        }

        private SettingsModel LoadSettings()
        {
            try
            {
                return GetServiceProvider()?.GetService<ISettingsService>()?.LoadSettingsOrDefault();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not read the settings: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Gets a service provider for the service. The service can run independently of the
        /// application, so it builds its own service provider once.
        /// </summary>
        /// <returns>The service provider, or null if it could not be built.</returns>
        private IServiceProvider GetServiceProvider()
        {
            if (_serviceProvider != null)
                return _serviceProvider;

            try
            {
                ServiceCollection services = new ServiceCollection();
                MauiProgram.RegisterSharedServices(services);
                MauiProgram.RegisterPlatformServices(services);
                ServiceProvider serviceProvider = services.BuildServiceProvider();
                serviceProvider.GetService<IAppContextService>().InitializeWithContextOnly(this);
                _serviceProvider = serviceProvider;
                return _serviceProvider;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not build the service provider: " + ex);
                return null;
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
            private bool _longPressFired;
            private System.Action _longPressAction;
            private float _touchSlop;

            public OverlayTouchListener(FloatingButtonAccessibilityService service)
            {
                _service = service;
            }

            /// <summary>
            /// Gets the movement which is tolerated while holding the button, before the gesture is
            /// treated as a drag. Uses the system touch slop (a real finger always drifts a little),
            /// doubled to make it comfortable to hold the button for a long press.
            /// </summary>
            private float GetTouchSlop(global::Android.Views.View view)
            {
                if (_touchSlop <= 0f)
                    _touchSlop = global::Android.Views.ViewConfiguration.Get(view.Context).ScaledTouchSlop * 2f;
                return _touchSlop;
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
                        _longPressFired = false;
                        ScheduleLongPress();
                        return true;

                    case MotionEventActions.Move:
                        float dx = motionEvent.RawX - _touchStartRawX;
                        float dy = motionEvent.RawY - _touchStartRawY;
                        float touchSlop = GetTouchSlop(view);
                        if (Math.Abs(dx) > touchSlop || Math.Abs(dy) > touchSlop)
                        {
                            _isDragging = true;
                            CancelLongPress();
                        }

                        if (_isDragging)
                        {
                            _service._overlayParams.X = _paramsStartX + (int)dx;
                            _service._overlayParams.Y = _paramsStartY + (int)dy;
                            _service._windowManager.UpdateViewLayout(view, _service._overlayParams);
                        }
                        return true;

                    case MotionEventActions.Up:
                        CancelLongPress();
                        if (_longPressFired)
                            return true; // The long press already captured the selected text.

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

                    case MotionEventActions.Cancel:
                        CancelLongPress();
                        return true;

                    default:
                        return true;
                }
            }

            private void ScheduleLongPress()
            {
                CancelLongPress();
                _longPressAction = () =>
                {
                    _longPressFired = true;
                    _service.CaptureSelectedText();
                };
                _service._gestureHandler?.PostDelayed(_longPressAction, LongPressDelayMs);
            }

            private void CancelLongPress()
            {
                if (_longPressAction != null)
                {
                    _service._gestureHandler?.RemoveCallbacks(_longPressAction);
                    _longPressAction = null;
                }
            }
        }
    }
}
