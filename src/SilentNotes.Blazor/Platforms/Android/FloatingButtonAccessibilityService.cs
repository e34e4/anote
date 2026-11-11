// Copyright © 2024 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Provider;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using Android.Views.InputMethods;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using SilentNotes.ViewModels;
using SilentNotes.Workers;
using RouteNames = SilentNotes.RouteNames;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;
using Uri = Android.Net.Uri;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// An accessibility service which draws a small floating button on top of other applications.
    /// Tapping the button opens a popup grid menu of all aNote tabs plus a Keyboard Switcher button.
    /// Swiping up/down/left/right, long-pressing, or double-clicking the button executes user-configured
    /// app launches or built-in actions.
    /// </summary>
    [Service(
        Exported = true,
        Label = "aNote",
        Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE")]
    [IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
    [MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service_config")]
    public class FloatingButtonAccessibilityService : AccessibilityService
    {
        /// <summary>Intent extra specifying which zero-based tab index to open in MainActivity.</summary>
        public const string ExtraOpenTab = "aNoteOpenTab";

        /// <summary>Intent extra requesting navigation to a Blazor route (e.g. /system).</summary>
        public const string ExtraOpenRoute = "aNoteOpenRoute";

        /// <summary>Intent extra requesting a screen-recording toggle (start or stop).</summary>
        public const string ExtraToggleScreenRecord = "aNoteToggleScreenRecord";

        private const string PreferencesName = "anote_floating_button";
        private const string PreferenceKeyX = "x";
        private const string PreferenceKeyY = "y";
        private const string PreferenceKeyMenuX = "menu_x";
        private const string PreferenceKeyMenuY = "menu_y";
        private const string PreferenceKeyMenuWidth = "menu_w";
        private const string PreferenceKeyMenuHeight = "menu_h";
        private const string PreferenceKeyImageX = "img_x";
        private const string PreferenceKeyImageY = "img_y";
        private const string PreferenceKeyImageWidth = "img_w";
        private const string PreferenceKeyImageHeight = "img_h";
        private const string PreferenceKeyVideoX = "vid_x";
        private const string PreferenceKeyVideoY = "vid_y";
        private const string PreferenceKeyVideoWidth = "vid_w";
        private const string PreferenceKeyVideoHeight = "vid_h";
        private const string LogTag = "aNoteFloating";
        private const int LongPressDelayMs = 600;
        private const int DoubleTapTimeoutMs = 260;
        private const int SwipeMaxDurationMs = 480;
        private const int SelectionMaxAgeSeconds = 120;

        private static readonly (int Index, string Icon, string Key, string Fallback)[] TabDefinitions = new[]
        {
            (20, "🎤", "tab_voice", "Voice"),
            (0, "📝", "tab_notes", "Notes"),
            (1, "📋", "tab_clipboard", "Clip"),
            (2, "🔖", "tab_booky", "Booky"),
            (3, "👤", "tab_phone", "Contacts"),
            (18, "📰", "tab_rss", "RSS"),
            (4, "🧮", "tab_calc", "Calc"),
            (5, "📅", "tab_cald", "Cald"),
            (6, "⏰", "tab_clock", "Clock"),
            (7, "💬", "tab_sms", "Sms"),
            (8, "🔳", "tab_qr", "QR"),
            (9, "🎙️", "tab_rec", "Rec"),
            (10, "📁", "tab_file", "File"),
            (11, "📞", "tab_dialer", "Dialer"),
            (12, "📷", "tab_camera", "Camera"),
            (13, "✉️", "tab_mail", "Mail"),
            (14, "▶️", "tab_player", "Player"),
            (15, "📖", "tab_reader", "Reader"),
            (16, "🔑", "tab_password", "Password"),
            (17, "✈", "tab_tg", "TG"),
            (19, "🌐", "tab_abro", "Abro"),
        };

        private const string PreferenceKeyShortcuts = "menu_shortcuts";
        private const string ShortcutTypeApp = "a";
        private const string ShortcutTypeFolder = "f";
        private const string DefaultFolderPath = "/storage/emulated/0/";

        private sealed class MenuShortcut
        {
            public string Type { get; set; }
            public string Value { get; set; }
            public string Label { get; set; }
        }

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
        private global::Android.Views.View _menuOverlayView;
        private WindowManagerLayoutParams _menuLayoutParams;
        private global::Android.Views.View _imageOverlayView;
        private WindowManagerLayoutParams _imageLayoutParams;
        private ImageView _imageOverlayImageView;
        private Bitmap _imageOverlayBitmap;
        private string _imageOverlayPath;
        private global::Android.Views.View _videoOverlayView;
        private WindowManagerLayoutParams _videoLayoutParams;
        private VideoView _videoOverlayVideoView;
        private string _videoOverlayPath;
        private bool _captureEnabled;
        private bool _menuShowingApps;
        private bool _menuLaunchAsFloating;

        private IServiceProvider _serviceProvider;
        private ClipboardCapture _clipboardCapture;
        private ClipboardManager _clipboardManager;
        private ClipboardChangedListener _clipboardListener;
        private Handler _gestureHandler;
        private ScreenshotCapture _screenshotCapture;
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

            // Keep root watchdog's component hint in sync (CRC class names change per build).
            AccessibilityServiceWatchdog.PublishComponentHint(this);

            EnsureOverlayMatchesSettings();

            if (_captureEnabled)
                RefreshFromActiveWindow();

            _gestureHandler = new Handler(Looper.MainLooper);
            RefreshClipboardSettings();

            // WindowManager can be briefly unavailable right after bind; retry overlay once.
            if (ShouldShowFloatingButton() && _overlayView == null && _gestureHandler != null)
            {
                _gestureHandler.PostDelayed(() =>
                {
                    try
                    {
                        if (Instance != this)
                            return;
                        if (_windowManager == null)
                            _windowManager = ResolveWindowManager();
                        EnsureOverlayMatchesSettings();
                    }
                    catch (Exception ex)
                    {
                        global::Android.Util.Log.Error(LogTag, "Overlay retry failed: " + ex);
                    }
                }, 750);
            }
        }

        /// <summary>
        /// Shows or hides the floating button according to current settings.
        /// Safe to call when the service is connected after a root heal.
        /// </summary>
        internal void EnsureOverlayMatchesSettings()
        {
            try
            {
                if (ShouldShowFloatingButton())
                    ShowOverlay();
                else
                    HideOverlay();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "EnsureOverlayMatchesSettings failed: " + ex);
            }
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
        internal void SetCaptureEnabled(bool enabled)
        {
            _captureEnabled = enabled;
            BrowserUrlTracker.CaptureEnabled = enabled;
        }

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

        private long _lastUrlRefreshMs;
        private string _lastUrlRefreshPackage;

        /// <inheritdoc/>
        public override void OnAccessibilityEvent(AccessibilityEvent e)
        {
            // Uncaught exceptions here disable the accessibility service (and often kill the process).
            try
            {
                if (e == null)
                    return;

                RememberTextSelection(e);

                if (!_captureEnabled)
                    return;

                // Skip noisy content-change floods; window state changes are enough for URL capture.
                if (e.EventType == EventTypes.ViewTextChanged ||
                    e.EventType == EventTypes.ViewTextSelectionChanged)
                    return;

                string packageName = e.PackageName?.ToString();
                if (string.IsNullOrEmpty(packageName) || !BrowserPackages.Contains(packageName))
                    return;

                // Throttle tree walks — OEMs disable the service when event handling is too heavy.
                long now = Java.Lang.JavaSystem.CurrentTimeMillis();
                if (packageName == _lastUrlRefreshPackage && (now - _lastUrlRefreshMs) < 400)
                    return;
                _lastUrlRefreshMs = now;
                _lastUrlRefreshPackage = packageName;

                RefreshFromPackage(packageName);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "OnAccessibilityEvent failed: " + ex);
            }
        }

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
            HideImageOverlay();
            HideVideoOverlay();
            HideMenuOverlay();
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
            HideImageOverlay();
            HideVideoOverlay();
            HideMenuOverlay();
            HideOverlay();
            Instance = null;
            BrowserUrlTracker.ServiceEnabled = false;
            BrowserUrlTracker.CaptureEnabled = false;
            base.OnDestroy();
        }

        private void RefreshClipboardSettings()
        {
            if (ShouldMonitorClipboard())
                StartClipboardCapture();
            else
                StopClipboardCapture();
        }

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
            if (string.IsNullOrEmpty(packageName))
                return;

            var roots = new List<AccessibilityNodeInfo>();
            try
            {
                AccessibilityNodeInfo activeRoot = RootInActiveWindow;
                if (activeRoot != null)
                    roots.Add(activeRoot);

                IList<AccessibilityWindowInfo> windows = Windows;
                if (windows != null)
                {
                    foreach (AccessibilityWindowInfo window in windows)
                    {
                        AccessibilityNodeInfo windowRoot = null;
                        try
                        {
                            windowRoot = window?.Root;
                        }
                        catch (Exception)
                        {
                            windowRoot = null;
                        }
                        if (windowRoot != null)
                            roots.Add(windowRoot);
                    }
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not collect accessibility roots: " + ex);
                return;
            }

            foreach (AccessibilityNodeInfo root in roots)
            {
                try
                {
                    string url = FindUrl(root, packageName);
                    if (!string.IsNullOrEmpty(url))
                    {
                        BrowserUrlTracker.Update(url, packageName);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error(LogTag, "FindUrl failed: " + ex);
                }
            }
        }

        private string FindUrl(AccessibilityNodeInfo root, string packageName)
        {
            if (root == null || string.IsNullOrEmpty(packageName))
                return null;

            foreach (string viewId in UrlViewIds)
            {
                IList<AccessibilityNodeInfo> nodes;
                try
                {
                    nodes = root.FindAccessibilityNodeInfosByViewId(packageName + ":id/" + viewId);
                }
                catch (Exception)
                {
                    continue;
                }
                if (nodes == null)
                    continue;
                foreach (AccessibilityNodeInfo node in nodes)
                {
                    if (node == null)
                        continue;
                    string url = ExtractUrl(node);
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }
            }
            return WalkForUrl(root, 0);
        }

        private static string ExtractUrl(AccessibilityNodeInfo node)
        {
            if (node == null)
                return null;

            var candidates = new List<string>();
            try
            {
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
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string candidate in candidates)
            {
                string normalized = NormalizeOmnibox(candidate);
                string url = BookyUrlUtils.FirstUrl(normalized);
                if (!string.IsNullOrEmpty(url))
                    return url;

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

            int childCount;
            try
            {
                childCount = node.ChildCount;
            }
            catch (Exception)
            {
                return null;
            }

            for (int index = 0; index < childCount; index++)
            {
                AccessibilityNodeInfo child;
                try
                {
                    child = node.GetChild(index);
                }
                catch (Exception)
                {
                    continue;
                }
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
                _windowManager = ResolveWindowManager();
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
            HideMenuOverlay();
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
        /// Toggles the popup grid menu containing buttons for all aNote tabs and the Keyboard Switcher.
        /// </summary>
        internal void ToggleMenuOverlay()
        {
            if (_menuOverlayView != null)
                HideMenuOverlay();
            else
                ShowMenuOverlay();
        }

        internal void ShowMenuOverlay()
        {
            if (_menuOverlayView != null || _windowManager == null)
                return;

            try
            {
                float density = Resources.DisplayMetrics.Density;
                int screenW = Resources.DisplayMetrics.WidthPixels;
                int screenH = Resources.DisplayMetrics.HeightPixels;
                ILanguageService language = GetServiceProvider()?.GetService<ILanguageService>();

                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                int defaultWidth = (int)Math.Min(screenW * 0.90f, 340 * density);
                int defaultHeight = Math.Max((int)(240 * density), screenH / 2);
                int minWidth = (int)(200 * density);
                int minHeight = (int)(160 * density);

                int winWidth = Math.Clamp(preferences.GetInt(PreferenceKeyMenuWidth, defaultWidth), minWidth, screenW);
                int winHeight = Math.Clamp(preferences.GetInt(PreferenceKeyMenuHeight, defaultHeight), minHeight, screenH);
                int defaultX = Math.Max(0, (screenW - winWidth) / 2);
                int defaultY = Math.Max(0, (screenH - winHeight) / 2);
                int winX = Math.Clamp(preferences.GetInt(PreferenceKeyMenuX, defaultX), 0, Math.Max(0, screenW - minWidth / 2));
                int winY = Math.Clamp(preferences.GetInt(PreferenceKeyMenuY, defaultY), 0, Math.Max(0, screenH - minHeight / 2));

                var card = new LinearLayout(this)
                {
                    Orientation = Orientation.Vertical,
                    Clickable = true,
                };
                int cardPad = (int)(10 * density);
                card.SetPadding(cardPad, cardPad, cardPad, (int)(6 * density));

                var cardBg = new GradientDrawable();
                cardBg.SetColor(Color.ParseColor("#F2101010"));
                cardBg.SetCornerRadius(16 * density);
                cardBg.SetStroke((int)(1.5f * density), Color.ParseColor("#38BDF8"));
                card.Background = cardBg;

                // Header row (draggable): Title + optional Apps/Float controls + Reset Size + Close
                var headerRow = new LinearLayout(this)
                {
                    Orientation = Orientation.Horizontal,
                };
                headerRow.SetGravity(GravityFlags.CenterVertical);
                headerRow.SetPadding((int)(4 * density), (int)(2 * density), (int)(4 * density), (int)(6 * density));
                int btnPad = (int)(6 * density);

                if (_menuShowingApps)
                {
                    var backBtn = new TextView(this)
                    {
                        Text = "←",
                        TextSize = 16f,
                        Gravity = GravityFlags.Center,
                    };
                    backBtn.SetTextColor(Color.ParseColor("#38BDF8"));
                    backBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                    backBtn.Click += (s, e) =>
                    {
                        _menuShowingApps = false;
                        RebuildMenuOverlay();
                    };
                    headerRow.AddView(backBtn);
                }

                var titleView = new TextView(this)
                {
                    Text = _menuShowingApps ? "Apps" : "⠿ aNote Quick Menu",
                    TextSize = 13.5f,
                };
                titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                titleView.SetTextColor(Color.White);
                headerRow.AddView(titleView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

                if (_menuShowingApps)
                {
                    var floatBtn = new TextView(this)
                    {
                        Text = _menuLaunchAsFloating ? "🪟 On" : "🪟",
                        TextSize = 12f,
                        Gravity = GravityFlags.Center,
                    };
                    floatBtn.SetTextColor(Color.ParseColor(_menuLaunchAsFloating ? "#38BDF8" : "#BDBDBD"));
                    floatBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                    floatBtn.Click += (s, e) =>
                    {
                        _menuLaunchAsFloating = !_menuLaunchAsFloating;
                        RebuildMenuOverlay();
                    };
                    headerRow.AddView(floatBtn);
                }

                var halfBtn = new TextView(this)
                {
                    Text = "½",
                    TextSize = 12f,
                    Gravity = GravityFlags.Center,
                };
                halfBtn.SetTextColor(Color.ParseColor("#38BDF8"));
                halfBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                halfBtn.Click += (s, e) => ResetMenuToHalfScreen(defaultWidth, defaultHeight, screenW, screenH);
                headerRow.AddView(halfBtn);

                var closeBtn = new TextView(this)
                {
                    Text = "✕",
                    TextSize = 14f,
                    Gravity = GravityFlags.Center,
                };
                closeBtn.SetTextColor(Color.ParseColor("#BDBDBD"));
                closeBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                closeBtn.Click += (s, e) => HideMenuOverlay();
                headerRow.AddView(closeBtn);

                headerRow.SetOnTouchListener(new MenuDragTouchListener(this));
                card.AddView(headerRow);

                var scrollView = new global::Android.Widget.ScrollView(this)
                {
                    FillViewport = true,
                };

                if (_menuShowingApps)
                {
                    PopulateAppsList(scrollView, density);
                }
                else
                {
                    PopulateMainMenuGrid(scrollView, language, density);
                }

                card.AddView(scrollView, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, 0, 1f));

                // Bottom resize bar: vertical drag bar + bottom-right 2D corner grip
                var footerRow = new LinearLayout(this)
                {
                    Orientation = Orientation.Horizontal,
                };
                footerRow.SetGravity(GravityFlags.CenterVertical);
                footerRow.SetPadding(0, (int)(4 * density), 0, 0);

                var bottomResizePill = new TextView(this)
                {
                    Text = "═══",
                    TextSize = 11f,
                    Gravity = GravityFlags.Center,
                };
                bottomResizePill.SetTextColor(Color.ParseColor("#757575"));
                bottomResizePill.SetOnTouchListener(new MenuResizeTouchListener(this, resizeWidth: false, resizeHeight: true, minWidth, minHeight, screenW, screenH));
                footerRow.AddView(bottomResizePill, new LinearLayout.LayoutParams(0, (int)(22 * density), 1f));

                var cornerGrip = new TextView(this)
                {
                    Text = "◢",
                    TextSize = 14f,
                    Gravity = GravityFlags.Center,
                };
                cornerGrip.SetTextColor(Color.ParseColor("#38BDF8"));
                cornerGrip.SetPadding((int)(8 * density), 0, (int)(4 * density), 0);
                cornerGrip.SetOnTouchListener(new MenuResizeTouchListener(this, resizeWidth: true, resizeHeight: true, minWidth, minHeight, screenW, screenH));
                footerRow.AddView(cornerGrip, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, (int)(22 * density)));

                card.AddView(footerRow, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

                _menuLayoutParams = new WindowManagerLayoutParams(
                    winWidth,
                    winHeight,
                    WindowManagerTypes.AccessibilityOverlay,
                    WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.Start,
                    X = winX,
                    Y = winY,
                };

                _menuOverlayView = card;
                _windowManager.AddView(_menuOverlayView, _menuLayoutParams);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show floating menu overlay: " + ex);
                _menuOverlayView = null;
                _menuLayoutParams = null;
            }
        }

        private void ResetMenuToHalfScreen(int defaultWidth, int defaultHeight, int screenW, int screenH)
        {
            if (_menuOverlayView == null || _menuLayoutParams == null || _windowManager == null)
                return;

            _menuLayoutParams.Width = defaultWidth;
            _menuLayoutParams.Height = defaultHeight;
            _menuLayoutParams.X = Math.Max(0, (screenW - defaultWidth) / 2);
            _menuLayoutParams.Y = Math.Max(0, (screenH - defaultHeight) / 2);
            try
            {
                _windowManager.UpdateViewLayout(_menuOverlayView, _menuLayoutParams);
                SaveMenuBounds();
            }
            catch (Exception)
            {
            }
        }

        private void SaveMenuBounds()
        {
            if (_menuLayoutParams == null)
                return;
            try
            {
                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                preferences.Edit()
                    .PutInt(PreferenceKeyMenuX, _menuLayoutParams.X)
                    .PutInt(PreferenceKeyMenuY, _menuLayoutParams.Y)
                    .PutInt(PreferenceKeyMenuWidth, _menuLayoutParams.Width)
                    .PutInt(PreferenceKeyMenuHeight, _menuLayoutParams.Height)
                    .Apply();
            }
            catch (Exception)
            {
            }
        }

        internal void HideMenuOverlay()
        {
            if (_menuOverlayView == null || _windowManager == null)
                return;

            try
            {
                _windowManager.RemoveView(_menuOverlayView);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not hide floating menu overlay: " + ex);
            }
            _menuOverlayView = null;
            _menuLayoutParams = null;
            _menuShowingApps = false;
        }

        /// <summary>
        /// Shows a movable/resizable floating image window using the same Accessibility overlay
        /// style as the quick menu. Returns false if the path is missing or the overlay cannot be shown.
        /// </summary>
        internal bool ShowImageOverlay(string path, string title = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return false;

            if (_windowManager == null)
                _windowManager = ResolveWindowManager();
            if (_windowManager == null)
                return false;

            try
            {
                HideImageOverlay();

                float density = Resources.DisplayMetrics.Density;
                int screenW = Resources.DisplayMetrics.WidthPixels;
                int screenH = Resources.DisplayMetrics.HeightPixels;

                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                int defaultWidth = (int)Math.Min(screenW * 0.92f, 420 * density);
                int defaultHeight = Math.Max((int)(280 * density), (int)(screenH * 0.55f));
                int minWidth = (int)(180 * density);
                int minHeight = (int)(160 * density);

                int winWidth = Math.Clamp(preferences.GetInt(PreferenceKeyImageWidth, defaultWidth), minWidth, screenW);
                int winHeight = Math.Clamp(preferences.GetInt(PreferenceKeyImageHeight, defaultHeight), minHeight, screenH);
                int defaultX = Math.Max(0, (screenW - winWidth) / 2);
                int defaultY = Math.Max(0, (screenH - winHeight) / 2);
                int winX = Math.Clamp(preferences.GetInt(PreferenceKeyImageX, defaultX), 0, Math.Max(0, screenW - minWidth / 2));
                int winY = Math.Clamp(preferences.GetInt(PreferenceKeyImageY, defaultY), 0, Math.Max(0, screenH - minHeight / 2));

                Bitmap bitmap = BitmapFactory.DecodeFile(path);
                if (bitmap == null)
                    return false;

                _imageOverlayBitmap = bitmap;
                _imageOverlayPath = path;

                var card = new LinearLayout(this)
                {
                    Orientation = Orientation.Vertical,
                    Clickable = true,
                };
                int cardPad = (int)(10 * density);
                card.SetPadding(cardPad, cardPad, cardPad, (int)(6 * density));

                var cardBg = new GradientDrawable();
                cardBg.SetColor(Color.ParseColor("#F2101010"));
                cardBg.SetCornerRadius(16 * density);
                cardBg.SetStroke((int)(1.5f * density), Color.ParseColor("#38BDF8"));
                card.Background = cardBg;

                var headerRow = new LinearLayout(this)
                {
                    Orientation = Orientation.Horizontal,
                };
                headerRow.SetGravity(GravityFlags.CenterVertical);
                headerRow.SetPadding((int)(4 * density), (int)(2 * density), (int)(4 * density), (int)(6 * density));
                int btnPad = (int)(6 * density);

                string headerTitle = string.IsNullOrWhiteSpace(title)
                    ? System.IO.Path.GetFileName(path)
                    : title;
                if (string.IsNullOrWhiteSpace(headerTitle))
                    headerTitle = "Image";

                var titleView = new TextView(this)
                {
                    Text = "⠿ " + headerTitle,
                    TextSize = 13.5f,
                    Ellipsize = global::Android.Text.TextUtils.TruncateAt.End,
                };
                titleView.SetSingleLine(true);
                titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                titleView.SetTextColor(Color.White);
                headerRow.AddView(titleView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

                var editBtn = new TextView(this)
                {
                    Text = "Edit",
                    TextSize = 12f,
                    Gravity = GravityFlags.Center,
                };
                editBtn.SetTextColor(Color.ParseColor("#38BDF8"));
                editBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                editBtn.Click += (s, e) => OpenImageOverlayEditor();
                headerRow.AddView(editBtn);

                var closeBtn = new TextView(this)
                {
                    Text = "✕",
                    TextSize = 14f,
                    Gravity = GravityFlags.Center,
                };
                closeBtn.SetTextColor(Color.ParseColor("#BDBDBD"));
                closeBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                closeBtn.Click += (s, e) => HideImageOverlay();
                headerRow.AddView(closeBtn);

                headerRow.SetOnTouchListener(new ImageDragTouchListener(this));
                card.AddView(headerRow);

                _imageOverlayImageView = new ImageView(this);
                _imageOverlayImageView.SetScaleType(ImageView.ScaleType.FitCenter);
                _imageOverlayImageView.SetImageBitmap(_imageOverlayBitmap);
                _imageOverlayImageView.SetBackgroundColor(Color.Black);
                // Drag from the picture area as well (same as moving the quick-menu window).
                _imageOverlayImageView.SetOnTouchListener(new ImageDragTouchListener(this));
                card.AddView(_imageOverlayImageView, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, 0, 1f));

                var footerRow = new LinearLayout(this)
                {
                    Orientation = Orientation.Horizontal,
                };
                footerRow.SetGravity(GravityFlags.CenterVertical);
                footerRow.SetPadding(0, (int)(4 * density), 0, 0);

                var bottomResizePill = new TextView(this)
                {
                    Text = "═══",
                    TextSize = 11f,
                    Gravity = GravityFlags.Center,
                };
                bottomResizePill.SetTextColor(Color.ParseColor("#757575"));
                bottomResizePill.SetOnTouchListener(new ImageResizeTouchListener(this, resizeWidth: false, resizeHeight: true, minWidth, minHeight, screenW, screenH));
                footerRow.AddView(bottomResizePill, new LinearLayout.LayoutParams(0, (int)(22 * density), 1f));

                var cornerGrip = new TextView(this)
                {
                    Text = "◢",
                    TextSize = 14f,
                    Gravity = GravityFlags.Center,
                };
                cornerGrip.SetTextColor(Color.ParseColor("#38BDF8"));
                cornerGrip.SetPadding((int)(8 * density), 0, (int)(4 * density), 0);
                cornerGrip.SetOnTouchListener(new ImageResizeTouchListener(this, resizeWidth: true, resizeHeight: true, minWidth, minHeight, screenW, screenH));
                footerRow.AddView(cornerGrip, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, (int)(22 * density)));

                card.AddView(footerRow, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

                _imageLayoutParams = new WindowManagerLayoutParams(
                    winWidth,
                    winHeight,
                    WindowManagerTypes.AccessibilityOverlay,
                    WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.Start,
                    X = winX,
                    Y = winY,
                };

                _imageOverlayView = card;
                _windowManager.AddView(_imageOverlayView, _imageLayoutParams);
                return true;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show floating image overlay: " + ex);
                HideImageOverlay();
                return false;
            }
        }

        internal void HideImageOverlay()
        {
            if (_imageOverlayView != null && _windowManager != null)
            {
                try
                {
                    _windowManager.RemoveView(_imageOverlayView);
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error(LogTag, "Could not hide floating image overlay: " + ex);
                }
            }

            _imageOverlayView = null;
            _imageLayoutParams = null;
            _imageOverlayImageView = null;
            _imageOverlayPath = null;

            try
            {
                _imageOverlayBitmap?.Recycle();
            }
            catch (Exception)
            {
            }
            _imageOverlayBitmap = null;
        }

        private void SaveImageBounds()
        {
            if (_imageLayoutParams == null)
                return;
            try
            {
                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                preferences.Edit()
                    .PutInt(PreferenceKeyImageX, _imageLayoutParams.X)
                    .PutInt(PreferenceKeyImageY, _imageLayoutParams.Y)
                    .PutInt(PreferenceKeyImageWidth, _imageLayoutParams.Width)
                    .PutInt(PreferenceKeyImageHeight, _imageLayoutParams.Height)
                    .Apply();
            }
            catch (Exception)
            {
            }
        }

        private void OpenImageOverlayEditor()
        {
            try
            {
                string path = _imageOverlayPath;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    return;

                Intent intent = SilentNotes.ImageEditorActivity.CreateIntent(this, path, floating: true);
                intent.AddFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not open image editor from overlay: " + ex);
            }
        }

        /// <summary>
        /// Shows a movable/resizable floating video player (accessibility overlay). Returns false on failure.
        /// </summary>
        internal bool ShowVideoOverlay(string path, string title = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return false;

            if (_windowManager == null)
                _windowManager = ResolveWindowManager();
            if (_windowManager == null)
                return false;

            try
            {
                HideVideoOverlay();

                float density = Resources.DisplayMetrics.Density;
                int screenW = Resources.DisplayMetrics.WidthPixels;
                int screenH = Resources.DisplayMetrics.HeightPixels;

                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                int defaultWidth = (int)Math.Min(screenW * 0.92f, 440 * density);
                int defaultHeight = Math.Max((int)(220 * density), (int)(screenH * 0.5f));
                int minWidth = (int)(200 * density);
                int minHeight = (int)(140 * density);

                int winWidth = Math.Clamp(preferences.GetInt(PreferenceKeyVideoWidth, defaultWidth), minWidth, screenW);
                int winHeight = Math.Clamp(preferences.GetInt(PreferenceKeyVideoHeight, defaultHeight), minHeight, screenH);
                int defaultX = Math.Max(0, (screenW - winWidth) / 2);
                int defaultY = Math.Max(0, (screenH - winHeight) / 2);
                int winX = Math.Clamp(preferences.GetInt(PreferenceKeyVideoX, defaultX), 0, Math.Max(0, screenW - minWidth / 2));
                int winY = Math.Clamp(preferences.GetInt(PreferenceKeyVideoY, defaultY), 0, Math.Max(0, screenH - minHeight / 2));

                _videoOverlayPath = path;

                var card = new LinearLayout(this)
                {
                    Orientation = Orientation.Vertical,
                    Clickable = true,
                };
                int cardPad = (int)(10 * density);
                card.SetPadding(cardPad, cardPad, cardPad, (int)(6 * density));

                var cardBg = new GradientDrawable();
                cardBg.SetColor(Color.ParseColor("#F2101010"));
                cardBg.SetCornerRadius(16 * density);
                cardBg.SetStroke((int)(1.5f * density), Color.ParseColor("#38BDF8"));
                card.Background = cardBg;

                var headerRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                headerRow.SetGravity(GravityFlags.CenterVertical);
                headerRow.SetPadding((int)(4 * density), (int)(2 * density), (int)(4 * density), (int)(6 * density));
                int btnPad = (int)(6 * density);

                string headerTitle = string.IsNullOrWhiteSpace(title)
                    ? System.IO.Path.GetFileName(path)
                    : title;
                if (string.IsNullOrWhiteSpace(headerTitle))
                    headerTitle = "Video";

                var titleView = new TextView(this)
                {
                    Text = "⠿ " + headerTitle,
                    TextSize = 13.5f,
                    Ellipsize = global::Android.Text.TextUtils.TruncateAt.End,
                };
                titleView.SetSingleLine(true);
                titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                titleView.SetTextColor(Color.White);
                headerRow.AddView(titleView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

                var editBtn = new TextView(this)
                {
                    Text = "Edit",
                    TextSize = 12f,
                    Gravity = GravityFlags.Center,
                };
                editBtn.SetTextColor(Color.ParseColor("#38BDF8"));
                editBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                editBtn.Click += (s, e) => OpenVideoOverlayEditor();
                headerRow.AddView(editBtn);

                var closeBtn = new TextView(this)
                {
                    Text = "✕",
                    TextSize = 14f,
                    Gravity = GravityFlags.Center,
                };
                closeBtn.SetTextColor(Color.ParseColor("#BDBDBD"));
                closeBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
                closeBtn.Click += (s, e) => HideVideoOverlay();
                headerRow.AddView(closeBtn);

                headerRow.SetOnTouchListener(new VideoDragTouchListener(this));
                card.AddView(headerRow);

                _videoOverlayVideoView = new VideoView(this);
                _videoOverlayVideoView.SetBackgroundColor(Color.Black);
                _videoOverlayVideoView.SetVideoPath(path);
                _videoOverlayVideoView.Start();
                card.AddView(_videoOverlayVideoView, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, 0, 1f));

                var footerRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                footerRow.SetGravity(GravityFlags.CenterVertical);
                footerRow.SetPadding(0, (int)(4 * density), 0, 0);

                var bottomResizePill = new TextView(this)
                {
                    Text = "═══",
                    TextSize = 11f,
                    Gravity = GravityFlags.Center,
                };
                bottomResizePill.SetTextColor(Color.ParseColor("#757575"));
                bottomResizePill.SetOnTouchListener(new VideoResizeTouchListener(this, resizeWidth: false, resizeHeight: true, minWidth, minHeight, screenW, screenH));
                footerRow.AddView(bottomResizePill, new LinearLayout.LayoutParams(0, (int)(22 * density), 1f));

                var cornerGrip = new TextView(this)
                {
                    Text = "◢",
                    TextSize = 14f,
                    Gravity = GravityFlags.Center,
                };
                cornerGrip.SetTextColor(Color.ParseColor("#38BDF8"));
                cornerGrip.SetPadding((int)(8 * density), 0, (int)(4 * density), 0);
                cornerGrip.SetOnTouchListener(new VideoResizeTouchListener(this, resizeWidth: true, resizeHeight: true, minWidth, minHeight, screenW, screenH));
                footerRow.AddView(cornerGrip, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, (int)(22 * density)));

                card.AddView(footerRow, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

                _videoLayoutParams = new WindowManagerLayoutParams(
                    winWidth,
                    winHeight,
                    WindowManagerTypes.AccessibilityOverlay,
                    WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.Start,
                    X = winX,
                    Y = winY,
                };

                _videoOverlayView = card;
                _windowManager.AddView(_videoOverlayView, _videoLayoutParams);
                return true;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show floating video overlay: " + ex);
                HideVideoOverlay();
                return false;
            }
        }

        internal void HideVideoOverlay()
        {
            try
            {
                _videoOverlayVideoView?.StopPlayback();
            }
            catch (Exception)
            {
            }

            if (_videoOverlayView != null && _windowManager != null)
            {
                try
                {
                    _windowManager.RemoveView(_videoOverlayView);
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Error(LogTag, "Could not hide floating video overlay: " + ex);
                }
            }

            _videoOverlayView = null;
            _videoLayoutParams = null;
            _videoOverlayVideoView = null;
            _videoOverlayPath = null;
        }

        private void SaveVideoBounds()
        {
            if (_videoLayoutParams == null)
                return;
            try
            {
                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                preferences.Edit()
                    .PutInt(PreferenceKeyVideoX, _videoLayoutParams.X)
                    .PutInt(PreferenceKeyVideoY, _videoLayoutParams.Y)
                    .PutInt(PreferenceKeyVideoWidth, _videoLayoutParams.Width)
                    .PutInt(PreferenceKeyVideoHeight, _videoLayoutParams.Height)
                    .Apply();
            }
            catch (Exception)
            {
            }
        }

        private void OpenVideoOverlayEditor()
        {
            try
            {
                string path = _videoOverlayPath;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    return;

                Intent intent = SilentNotes.VideoEditorActivity.CreateIntent(this, path, floating: true);
                intent.AddFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not open video editor from overlay: " + ex);
            }
        }

        private class VideoDragTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private float _startRawX;
            private float _startRawY;
            private int _startX;
            private int _startY;

            public VideoDragTouchListener(FloatingButtonAccessibilityService service)
            {
                _service = service;
            }

            public bool OnTouch(global::Android.Views.View view, MotionEvent e)
            {
                if (_service._videoOverlayView == null || _service._videoLayoutParams == null || _service._windowManager == null)
                    return false;

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _startRawX = e.RawX;
                        _startRawY = e.RawY;
                        _startX = _service._videoLayoutParams.X;
                        _startY = _service._videoLayoutParams.Y;
                        return true;

                    case MotionEventActions.Move:
                        int dx = (int)(e.RawX - _startRawX);
                        int dy = (int)(e.RawY - _startRawY);
                        _service._videoLayoutParams.X = Math.Max(0, _startX + dx);
                        _service._videoLayoutParams.Y = Math.Max(0, _startY + dy);
                        try
                        {
                            _service._windowManager.UpdateViewLayout(_service._videoOverlayView, _service._videoLayoutParams);
                        }
                        catch (Exception)
                        {
                        }
                        return true;

                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _service.SaveVideoBounds();
                        return true;
                }
                return false;
            }
        }

        private class VideoResizeTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private readonly bool _resizeWidth;
            private readonly bool _resizeHeight;
            private readonly int _minWidth;
            private readonly int _minHeight;
            private readonly int _maxWidth;
            private readonly int _maxHeight;
            private float _startRawX;
            private float _startRawY;
            private int _startWidth;
            private int _startHeight;

            public VideoResizeTouchListener(
                FloatingButtonAccessibilityService service,
                bool resizeWidth,
                bool resizeHeight,
                int minWidth,
                int minHeight,
                int maxWidth,
                int maxHeight)
            {
                _service = service;
                _resizeWidth = resizeWidth;
                _resizeHeight = resizeHeight;
                _minWidth = minWidth;
                _minHeight = minHeight;
                _maxWidth = maxWidth;
                _maxHeight = maxHeight;
            }

            public bool OnTouch(global::Android.Views.View view, MotionEvent e)
            {
                if (_service._videoOverlayView == null || _service._videoLayoutParams == null || _service._windowManager == null)
                    return false;

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _startRawX = e.RawX;
                        _startRawY = e.RawY;
                        _startWidth = _service._videoLayoutParams.Width;
                        _startHeight = _service._videoLayoutParams.Height;
                        return true;

                    case MotionEventActions.Move:
                        int dw = (int)(e.RawX - _startRawX);
                        int dh = (int)(e.RawY - _startRawY);
                        if (_resizeWidth)
                            _service._videoLayoutParams.Width = Math.Clamp(_startWidth + dw, _minWidth, _maxWidth);
                        if (_resizeHeight)
                            _service._videoLayoutParams.Height = Math.Clamp(_startHeight + dh, _minHeight, _maxHeight);
                        try
                        {
                            _service._windowManager.UpdateViewLayout(_service._videoOverlayView, _service._videoLayoutParams);
                        }
                        catch (Exception)
                        {
                        }
                        return true;

                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _service.SaveVideoBounds();
                        return true;
                }
                return false;
            }
        }

        private class ImageDragTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private float _startRawX;
            private float _startRawY;
            private int _startX;
            private int _startY;

            public ImageDragTouchListener(FloatingButtonAccessibilityService service)
            {
                _service = service;
            }

            public bool OnTouch(global::Android.Views.View view, MotionEvent e)
            {
                if (_service._imageOverlayView == null || _service._imageLayoutParams == null || _service._windowManager == null)
                    return false;

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _startRawX = e.RawX;
                        _startRawY = e.RawY;
                        _startX = _service._imageLayoutParams.X;
                        _startY = _service._imageLayoutParams.Y;
                        return true;

                    case MotionEventActions.Move:
                        int dx = (int)(e.RawX - _startRawX);
                        int dy = (int)(e.RawY - _startRawY);
                        _service._imageLayoutParams.X = Math.Max(0, _startX + dx);
                        _service._imageLayoutParams.Y = Math.Max(0, _startY + dy);
                        try
                        {
                            _service._windowManager.UpdateViewLayout(_service._imageOverlayView, _service._imageLayoutParams);
                        }
                        catch (Exception)
                        {
                        }
                        return true;

                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _service.SaveImageBounds();
                        return true;
                }
                return false;
            }
        }

        private class ImageResizeTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private readonly bool _resizeWidth;
            private readonly bool _resizeHeight;
            private readonly int _minWidth;
            private readonly int _minHeight;
            private readonly int _maxWidth;
            private readonly int _maxHeight;
            private float _startRawX;
            private float _startRawY;
            private int _startWidth;
            private int _startHeight;

            public ImageResizeTouchListener(
                FloatingButtonAccessibilityService service,
                bool resizeWidth,
                bool resizeHeight,
                int minWidth,
                int minHeight,
                int maxWidth,
                int maxHeight)
            {
                _service = service;
                _resizeWidth = resizeWidth;
                _resizeHeight = resizeHeight;
                _minWidth = minWidth;
                _minHeight = minHeight;
                _maxWidth = maxWidth;
                _maxHeight = maxHeight;
            }

            public bool OnTouch(global::Android.Views.View view, MotionEvent e)
            {
                if (_service._imageOverlayView == null || _service._imageLayoutParams == null || _service._windowManager == null)
                    return false;

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _startRawX = e.RawX;
                        _startRawY = e.RawY;
                        _startWidth = _service._imageLayoutParams.Width;
                        _startHeight = _service._imageLayoutParams.Height;
                        return true;

                    case MotionEventActions.Move:
                        if (_resizeWidth)
                        {
                            int dw = (int)(e.RawX - _startRawX);
                            _service._imageLayoutParams.Width = Math.Clamp(_startWidth + dw, _minWidth, _maxWidth);
                        }
                        if (_resizeHeight)
                        {
                            int dh = (int)(e.RawY - _startRawY);
                            _service._imageLayoutParams.Height = Math.Clamp(_startHeight + dh, _minHeight, _maxHeight);
                        }
                        try
                        {
                            _service._windowManager.UpdateViewLayout(_service._imageOverlayView, _service._imageLayoutParams);
                        }
                        catch (Exception)
                        {
                        }
                        return true;

                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _service.SaveImageBounds();
                        return true;
                }
                return false;
            }
        }

        private class MenuDragTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private float _startRawX;
            private float _startRawY;
            private int _startX;
            private int _startY;

            public MenuDragTouchListener(FloatingButtonAccessibilityService service)
            {
                _service = service;
            }

            public bool OnTouch(global::Android.Views.View view, MotionEvent e)
            {
                if (_service._menuOverlayView == null || _service._menuLayoutParams == null || _service._windowManager == null)
                    return false;

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _startRawX = e.RawX;
                        _startRawY = e.RawY;
                        _startX = _service._menuLayoutParams.X;
                        _startY = _service._menuLayoutParams.Y;
                        return true;

                    case MotionEventActions.Move:
                        int dx = (int)(e.RawX - _startRawX);
                        int dy = (int)(e.RawY - _startRawY);
                        _service._menuLayoutParams.X = Math.Max(0, _startX + dx);
                        _service._menuLayoutParams.Y = Math.Max(0, _startY + dy);
                        try
                        {
                            _service._windowManager.UpdateViewLayout(_service._menuOverlayView, _service._menuLayoutParams);
                        }
                        catch (Exception)
                        {
                        }
                        return true;

                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _service.SaveMenuBounds();
                        return true;
                }
                return false;
            }
        }

        private class MenuResizeTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private readonly bool _resizeWidth;
            private readonly bool _resizeHeight;
            private readonly int _minWidth;
            private readonly int _minHeight;
            private readonly int _maxWidth;
            private readonly int _maxHeight;
            private float _startRawX;
            private float _startRawY;
            private int _startWidth;
            private int _startHeight;

            public MenuResizeTouchListener(
                FloatingButtonAccessibilityService service,
                bool resizeWidth,
                bool resizeHeight,
                int minWidth,
                int minHeight,
                int maxWidth,
                int maxHeight)
            {
                _service = service;
                _resizeWidth = resizeWidth;
                _resizeHeight = resizeHeight;
                _minWidth = minWidth;
                _minHeight = minHeight;
                _maxWidth = maxWidth;
                _maxHeight = maxHeight;
            }

            public bool OnTouch(global::Android.Views.View view, MotionEvent e)
            {
                if (_service._menuOverlayView == null || _service._menuLayoutParams == null || _service._windowManager == null)
                    return false;

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _startRawX = e.RawX;
                        _startRawY = e.RawY;
                        _startWidth = _service._menuLayoutParams.Width;
                        _startHeight = _service._menuLayoutParams.Height;
                        return true;

                    case MotionEventActions.Move:
                        if (_resizeWidth)
                        {
                            int dw = (int)(e.RawX - _startRawX);
                            _service._menuLayoutParams.Width = Math.Clamp(_startWidth + dw, _minWidth, _maxWidth);
                        }
                        if (_resizeHeight)
                        {
                            int dh = (int)(e.RawY - _startRawY);
                            _service._menuLayoutParams.Height = Math.Clamp(_startHeight + dh, _minHeight, _maxHeight);
                        }
                        try
                        {
                            _service._windowManager.UpdateViewLayout(_service._menuOverlayView, _service._menuLayoutParams);
                        }
                        catch (Exception)
                        {
                        }
                        return true;

                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _service.SaveMenuBounds();
                        return true;
                }
                return false;
            }
        }

        private void PopulateMainMenuGrid(global::Android.Widget.ScrollView scrollView, ILanguageService language, float density)
        {
            var gridContainer = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };

            const int columns = 4;
            var allButtons = new List<(string Icon, string Label, System.Action OnClick, bool IsAccent, System.Action OnLongClick)>();

            foreach (var tab in TabDefinitions)
            {
                int tabIdx = tab.Index;
                string label = language?[tab.Key];
                if (string.IsNullOrWhiteSpace(label) || label == tab.Key)
                    label = tab.Fallback;
                allButtons.Add((tab.Icon, label, () =>
                {
                    HideMenuOverlay();
                    if (tabIdx == AbroViewModel.TabIndex)
                        OpenAbroBrowser();
                    else if (tabIdx == VoiceViewModel.TabIndex)
                        OpenVoiceCapture();
                    else
                        OpenApplicationAtTab(tabIdx);
                }, false, null));
            }

            // Custom shortcuts (apps and folders) added by the user, long-press removes them
            foreach (MenuShortcut shortcut in LoadShortcuts())
            {
                MenuShortcut entry = shortcut;
                bool isFolder = entry.Type == ShortcutTypeFolder;
                allButtons.Add((isFolder ? "📂" : "📱", entry.Label, () =>
                {
                    HideMenuOverlay();
                    if (isFolder)
                        LaunchFolder(entry.Value);
                    else
                        LaunchInstalledApp(entry.Value);
                }, false, () =>
                {
                    RemoveShortcut(entry);
                    Toast.MakeText(this, "Removed", ToastLength.Short)?.Show();
                    RebuildMenuOverlay();
                }));
            }

            string kbLabel = language?["floating_menu_keyboard"];
            if (string.IsNullOrWhiteSpace(kbLabel) || kbLabel == "floating_menu_keyboard")
                kbLabel = "Keyboard";
            allButtons.Add(("⌨️", kbLabel, () =>
            {
                HideMenuOverlay();
                ShowKeyboardSwitcher();
            }, true, null));

            string shotFullLabel = language?["floating_menu_screenshot_full"];
            if (string.IsNullOrWhiteSpace(shotFullLabel) || shotFullLabel == "floating_menu_screenshot_full")
                shotFullLabel = "Full screen";
            allButtons.Add(("📷", shotFullLabel, () =>
            {
                HideMenuOverlay();
                StartScreenshot(partial: false);
            }, true, null));

            bool screenRecording = ScreenRecordService.IsRecording;
            string screenRecLabel = language?["floating_menu_screen_record"];
            if (string.IsNullOrWhiteSpace(screenRecLabel) || screenRecLabel == "floating_menu_screen_record")
                screenRecLabel = screenRecording ? "Stop record" : "Screen rec";
            allButtons.Add((screenRecording ? "⏹️" : "🎥", screenRecLabel, () =>
            {
                HideMenuOverlay();
                ToggleScreenRecording();
            }, true, null));

            allButtons.Add(("📦", "Apps", () =>
            {
                _menuShowingApps = true;
                RebuildMenuOverlay();
            }, true, null));

            string systemLabel = language?["show_system"];
            if (string.IsNullOrWhiteSpace(systemLabel) || systemLabel == "show_system")
                systemLabel = "System";
            allButtons.Add(("🖥", systemLabel, () =>
            {
                HideMenuOverlay();
                OpenSystemPage();
            }, true, null));

            allButtons.Add(("➕", "+ App", () => ShowAddAppDialog(), true, null));
            allButtons.Add(("➕", "+ Folder", () => ShowAddFolderDialog(), true, null));

            for (int i = 0; i < allButtons.Count; i += columns)
            {
                var row = new LinearLayout(this)
                {
                    Orientation = Orientation.Horizontal,
                };

                for (int col = 0; col < columns; col++)
                {
                    int idx = i + col;
                    var cellParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                    int margin = (int)(3 * density);
                    cellParams.SetMargins(margin, margin, margin, margin);

                    if (idx < allButtons.Count)
                    {
                        var item = allButtons[idx];
                        global::Android.Views.View btnView = CreateMenuGridButton(item.Icon, item.Label, item.IsAccent, item.OnClick, density, item.OnLongClick);
                        row.AddView(btnView, cellParams);
                    }
                    else
                    {
                        row.AddView(new global::Android.Views.View(this), cellParams);
                    }
                }

                gridContainer.AddView(row, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            }

            scrollView.AddView(gridContainer, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        private void PopulateAppsList(global::Android.Widget.ScrollView scrollView, float density)
        {
            var gridContainer = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };

            var hint = new TextView(this)
            {
                Text = _menuLaunchAsFloating
                    ? "Floating on — tap to open as a window"
                    : "Tap to open · long-press for floating",
                TextSize = 10f,
            };
            hint.SetTextColor(Color.ParseColor("#94A3B8"));
            hint.SetPadding(0, 0, 0, (int)(4 * density));
            gridContainer.AddView(hint);

            List<(string Package, string Label, Drawable Icon)> apps = QueryLauncherApps();
            if (apps.Count == 0)
            {
                var empty = new TextView(this)
                {
                    Text = "No apps found",
                    TextSize = 13f,
                    Gravity = GravityFlags.Center,
                };
                empty.SetTextColor(Color.ParseColor("#BDBDBD"));
                empty.SetPadding(0, (int)(16 * density), 0, (int)(16 * density));
                gridContainer.AddView(empty);
            }
            else
            {
                const int columns = 4;
                for (int i = 0; i < apps.Count; i += columns)
                {
                    var row = new LinearLayout(this)
                    {
                        Orientation = Orientation.Horizontal,
                    };

                    for (int col = 0; col < columns; col++)
                    {
                        int idx = i + col;
                        var cellParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                        int margin = (int)(2 * density);
                        cellParams.SetMargins(margin, margin, margin, margin);

                        if (idx < apps.Count)
                        {
                            var app = apps[idx];
                            string packageName = app.Package;
                            global::Android.Views.View cell = CreateAppGridButton(
                                app.Icon,
                                app.Label,
                                () =>
                                {
                                    HideMenuOverlay();
                                    LaunchInstalledApp(packageName, asFloating: _menuLaunchAsFloating);
                                },
                                () =>
                                {
                                    HideMenuOverlay();
                                    LaunchInstalledApp(packageName, asFloating: !_menuLaunchAsFloating);
                                },
                                density);
                            row.AddView(cell, cellParams);
                        }
                        else
                        {
                            row.AddView(new global::Android.Views.View(this), cellParams);
                        }
                    }

                    gridContainer.AddView(row, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
                }
            }

            scrollView.AddView(gridContainer, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        private global::Android.Views.View CreateAppGridButton(
            Drawable icon,
            string label,
            System.Action onClick,
            System.Action onLongClick,
            float density)
        {
            var box = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                Clickable = true,
            };
            box.SetGravity(GravityFlags.CenterHorizontal);
            int padV = (int)(6 * density);
            int padH = (int)(2 * density);
            box.SetPadding(padH, padV, padH, padV);

            var bg = new GradientDrawable();
            bg.SetCornerRadius(10 * density);
            bg.SetColor(Color.ParseColor("#1C1C1C"));
            bg.SetStroke((int)(1 * density), Color.ParseColor("#2E2E2E"));
            box.Background = bg;

            int iconSize = (int)(36 * density);
            var iconView = new ImageView(this);
            iconView.SetScaleType(ImageView.ScaleType.FitCenter);
            if (icon != null)
                iconView.SetImageDrawable(icon);
            else
                iconView.SetImageResource(global::Android.Resource.Drawable.SymDefAppIcon);
            box.AddView(iconView, new LinearLayout.LayoutParams(iconSize, iconSize));

            var labelTv = new TextView(this)
            {
                Text = label ?? string.Empty,
                TextSize = 8f,
                Gravity = GravityFlags.CenterHorizontal,
            };
            labelTv.SetTextColor(Color.White);
            labelTv.SetMaxLines(2);
            labelTv.SetLines(2);
            labelTv.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            labelTv.SetPadding(0, (int)(4 * density), 0, 0);
            box.AddView(labelTv, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            box.Click += (s, e) => onClick?.Invoke();
            if (onLongClick != null)
            {
                box.LongClick += (s, e) =>
                {
                    e.Handled = true;
                    onLongClick();
                };
            }
            return box;
        }

        private List<(string Package, string Label, Drawable Icon)> QueryLauncherApps()
        {
            var entries = new List<(string Package, string Label, Drawable Icon)>();
            try
            {
                PackageManager pm = PackageManager;
                if (pm == null)
                    return entries;

                var mainIntent = new Intent(Intent.ActionMain, null);
                mainIntent.AddCategory(Intent.CategoryLauncher);

                IList<ResolveInfo> apps;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    apps = pm.QueryIntentActivities(
                        mainIntent,
                        PackageManager.ResolveInfoFlags.Of((long)PackageInfoFlags.MatchAll));
                }
                else
                {
                    #pragma warning disable CS0618
                    apps = pm.QueryIntentActivities(mainIntent, PackageInfoFlags.MatchAll);
                    #pragma warning restore CS0618
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (apps != null)
                {
                    foreach (ResolveInfo info in apps)
                    {
                        string pkg = info?.ActivityInfo?.PackageName;
                        if (string.IsNullOrEmpty(pkg) || !seen.Add(pkg))
                            continue;

                        string label = info.LoadLabel(pm)?.ToString();
                        if (string.IsNullOrWhiteSpace(label))
                            label = pkg;

                        Drawable icon = null;
                        try
                        {
                            icon = info.LoadIcon(pm);
                        }
                        catch (Exception)
                        {
                        }

                        entries.Add((pkg, label, icon));
                    }
                }

                entries = entries.OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not query launcher apps: " + ex);
            }
            return entries;
        }

        private global::Android.Views.View CreateMenuGridButton(
            string icon,
            string label,
            bool isAccent,
            System.Action onClick,
            float density,
            System.Action onLongClick = null)
        {
            var box = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            box.SetGravity(GravityFlags.Center);
            int padV = (int)(8 * density);
            int padH = (int)(4 * density);
            box.SetPadding(padH, padV, padH, padV);

            var bg = new GradientDrawable();
            bg.SetCornerRadius(10 * density);
            bg.SetColor(Color.ParseColor(isAccent ? "#1E293B" : "#1C1C1C"));
            bg.SetStroke((int)(1 * density), Color.ParseColor(isAccent ? "#38BDF8" : "#2E2E2E"));
            box.Background = bg;

            var iconTv = new TextView(this)
            {
                Text = icon,
                TextSize = 18f,
                Gravity = GravityFlags.Center,
            };
            box.AddView(iconTv);

            var labelTv = new TextView(this)
            {
                Text = label,
                TextSize = 11f,
                Gravity = GravityFlags.Center,
            };
            labelTv.SetSingleLine(true);
            labelTv.SetTextColor(Color.ParseColor(isAccent ? "#38BDF8" : "#FFFFFF"));
            box.AddView(labelTv);

            box.Click += (s, e) => onClick?.Invoke();
            if (onLongClick != null)
            {
                box.LongClick += (s, e) =>
                {
                    e.Handled = true;
                    onLongClick();
                };
            }
            return box;
        }

        /// <summary>
        /// Displays the system keyboard switcher dialog (like Kunzisoft's KeyboardSwitcher app).
        /// </summary>
        internal void ShowKeyboardSwitcher()
        {
            try
            {
                // First attempt direct InputMethodManager call, then open KeyboardSwitcherActivity
                // which acquires foreground window focus required on Android 9+ to show the picker.
                var imm = GetSystemService(InputMethodService) as InputMethodManager;
                imm?.ShowInputMethodPicker();
            }
            catch (Exception)
            {
            }

            try
            {
                var intent = new Intent(this, typeof(KeyboardSwitcherActivity));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.NoAnimation);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not launch KeyboardSwitcherActivity: " + ex);
            }
        }

        /// <summary>
        /// Executes a gesture action configured in Settings (swipe up/down/left/right, long press,
        /// or double click).
        /// </summary>
        internal bool ExecuteConfiguredGesture(string actionValue)
        {
            if (string.IsNullOrWhiteSpace(actionValue) || string.Equals(actionValue, ":none", StringComparison.OrdinalIgnoreCase))
                return false;

            string trimmed = actionValue.Trim();
            try
            {
                if (string.Equals(trimmed, ":capture_text", StringComparison.OrdinalIgnoreCase))
                {
                    CaptureSelectedText();
                    return true;
                }
                if (string.Equals(trimmed, ":screenshot_full", StringComparison.OrdinalIgnoreCase))
                {
                    StartScreenshot(partial: false);
                    return true;
                }
                if (string.Equals(trimmed, ":screenshot_partial", StringComparison.OrdinalIgnoreCase))
                {
                    StartScreenshot(partial: true);
                    return true;
                }
                if (string.Equals(trimmed, ":open_anote", StringComparison.OrdinalIgnoreCase))
                {
                    OpenApplication();
                    return true;
                }
                if (string.Equals(trimmed, ":show_menu", StringComparison.OrdinalIgnoreCase))
                {
                    ToggleMenuOverlay();
                    return true;
                }
                if (string.Equals(trimmed, ":keyboard_switcher", StringComparison.OrdinalIgnoreCase))
                {
                    ShowKeyboardSwitcher();
                    return true;
                }
                if (trimmed.StartsWith(":tab:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed.Substring(":tab:".Length), out int tabIdx))
                    {
                        if (tabIdx == AbroViewModel.TabIndex)
                            OpenAbroBrowser();
                        else if (tabIdx == VoiceViewModel.TabIndex)
                            OpenVoiceCapture();
                        else
                            OpenApplicationAtTab(tabIdx);
                        return true;
                    }
                }

                return LaunchInstalledApp(trimmed);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not execute gesture action '" + trimmed + "': " + ex);
                return false;
            }
        }

        private bool LaunchInstalledApp(string packageName, bool asFloating = false)
        {
            try
            {
                PackageManager pm = PackageManager;
                if (pm == null)
                    return false;

                Intent launchIntent = pm.GetLaunchIntentForPackage(packageName);
                if (launchIntent == null)
                {
                    var queryIntent = new Intent(Intent.ActionMain, null);
                    queryIntent.AddCategory(Intent.CategoryLauncher);
                    queryIntent.SetPackage(packageName);
                    #pragma warning disable CS0618
                    IList<ResolveInfo> matches = pm.QueryIntentActivities(queryIntent, 0);
                    #pragma warning restore CS0618
                    ActivityInfo act = matches?.FirstOrDefault()?.ActivityInfo;
                    if (act != null)
                    {
                        launchIntent = new Intent(Intent.ActionMain);
                        launchIntent.AddCategory(Intent.CategoryLauncher);
                        launchIntent.SetComponent(new ComponentName(act.PackageName, act.Name));
                    }
                }

                if (launchIntent == null)
                {
                    Toast.MakeText(this, "Cannot launch " + packageName, ToastLength.Short)?.Show();
                    return false;
                }

                launchIntent.AddFlags(ActivityFlags.NewTask);

                if (asFloating)
                {
                    if (TryStartAsFloatingWindow(launchIntent))
                        return true;

                    // Softer multi-window fallback when freeform is unavailable.
                    try
                    {
                        Intent adjacent = new Intent(launchIntent);
                        adjacent.AddFlags(ActivityFlags.LaunchAdjacent | ActivityFlags.MultipleTask);
                        StartActivity(adjacent);
                        return true;
                    }
                    catch (Exception)
                    {
                    }
                }

                StartActivity(launchIntent);
                return true;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Failed to launch app " + packageName + ": " + ex);
                return false;
            }
        }

        /// <summary>
        /// Tries to start an activity in freeform / floating window mode with launch bounds.
        /// Works on devices that support freeform (desktop mode, some OEMs, Chrome OS).
        /// </summary>
        private bool TryStartAsFloatingWindow(Intent launchIntent)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.N)
                return false;

            try
            {
                ActivityOptions options = ActivityOptions.MakeBasic();

                // Hidden API: setLaunchWindowingMode(WINDOWING_MODE_FREEFORM = 5)
                try
                {
                    Java.Lang.Reflect.Method method = options.Class.GetMethod(
                        "setLaunchWindowingMode",
                        Java.Lang.Integer.Type);
                    method.Invoke(options, new Java.Lang.Object[] { new Java.Lang.Integer(5) });
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Warn(LogTag, "setLaunchWindowingMode unavailable: " + ex.Message);
                }

                // OEM extras used by Samsung / some freeform launchers
                launchIntent.PutExtra("android.activity.windowingMode", 5);

                int w = Resources.DisplayMetrics.WidthPixels;
                int h = Resources.DisplayMetrics.HeightPixels;
                int left = w / 10;
                int top = h / 10;
                int right = w - left;
                int bottom = Math.Max(top + h / 3, (int)(h * 0.62f));
                options.SetLaunchBounds(new global::Android.Graphics.Rect(left, top, right, bottom));

                StartActivity(launchIntent, options.ToBundle());
                return true;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag, "Floating launch failed: " + ex.Message);
                return false;
            }
        }

        private List<MenuShortcut> LoadShortcuts()
        {
            var result = new List<MenuShortcut>();
            try
            {
                var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                string json = preferences.GetString(PreferenceKeyShortcuts, null);
                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                        return result;

                    foreach (JsonElement item in doc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                            continue;

                        string type = item.TryGetProperty("t", out JsonElement t) ? t.GetString() : null;
                        string value = item.TryGetProperty("v", out JsonElement v) ? v.GetString() : null;
                        string label = item.TryGetProperty("l", out JsonElement l) ? l.GetString() : null;
                        if (string.IsNullOrWhiteSpace(value) || (type != ShortcutTypeApp && type != ShortcutTypeFolder))
                            continue;

                        result.Add(new MenuShortcut
                        {
                            Type = type,
                            Value = value,
                            Label = string.IsNullOrWhiteSpace(label) ? value : label,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not load the menu shortcuts: " + ex);
            }
            return result;
        }

        private void SaveShortcuts(IList<MenuShortcut> shortcuts)
        {
            try
            {
                using (var stream = new System.IO.MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartArray();
                        foreach (MenuShortcut shortcut in shortcuts)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("t", shortcut.Type);
                            writer.WriteString("v", shortcut.Value);
                            writer.WriteString("l", shortcut.Label);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }

                    string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                    var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                    preferences.Edit().PutString(PreferenceKeyShortcuts, json).Apply();
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not save the menu shortcuts: " + ex);
            }
        }

        private void AddShortcut(string type, string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            List<MenuShortcut> shortcuts = LoadShortcuts();
            if (!shortcuts.Any(x => x.Type == type && string.Equals(x.Value, value, StringComparison.Ordinal)))
            {
                shortcuts.Add(new MenuShortcut
                {
                    Type = type,
                    Value = value,
                    Label = string.IsNullOrWhiteSpace(label) ? value : label,
                });
                SaveShortcuts(shortcuts);
            }
        }

        private void RemoveShortcut(MenuShortcut shortcut)
        {
            if (shortcut == null)
                return;

            List<MenuShortcut> shortcuts = LoadShortcuts();
            shortcuts.RemoveAll(x => x.Type == shortcut.Type && string.Equals(x.Value, shortcut.Value, StringComparison.Ordinal));
            SaveShortcuts(shortcuts);
        }

        private void RebuildMenuOverlay()
        {
            bool showingApps = _menuShowingApps;
            bool launchFloating = _menuLaunchAsFloating;
            HideMenuOverlay();
            _menuShowingApps = showingApps;
            _menuLaunchAsFloating = launchFloating;
            ShowMenuOverlay();
        }

        private bool LaunchFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Trim();
            if (normalized.Length > 1)
                normalized = normalized.TrimEnd('/');

            // 1. Storage access framework document uri, understood by the system file manager
            try
            {
                string documentId = ToDocumentId(normalized);
                if (documentId != null)
                {
                    Uri documentUri = DocumentsContract.BuildDocumentUri("com.android.externalstorage.documents", documentId);
                    var intent = new Intent(Intent.ActionView);
                    intent.SetDataAndType(documentUri, "vnd.android.document/directory");
                    intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
                    StartActivity(intent);
                    return true;
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn(LogTag, "Document uri folder launch failed: " + ex.Message);
            }

            // 2. Plain file uri and the legacy "resource/folder" mime type
            foreach (string mime in new[] { "resource/folder", "vnd.android.document/directory" })
            {
                try
                {
                    var intent = new Intent(Intent.ActionView);
                    intent.SetDataAndType(Uri.FromFile(new Java.IO.File(normalized)), mime);
                    intent.AddFlags(ActivityFlags.NewTask);
                    StartActivity(intent);
                    return true;
                }
                catch (Exception ex)
                {
                    global::Android.Util.Log.Warn(LogTag, "Folder launch with " + mime + " failed: " + ex.Message);
                }
            }

            Toast.MakeText(this, "Cannot open " + normalized, ToastLength.Short)?.Show();
            return false;
        }

        /// <summary>
        /// Converts a path like /storage/emulated/0/Download to the document id "primary:Download".
        /// </summary>
        private static string ToDocumentId(string path)
        {
            string[] primaryRoots = new[] { "/storage/emulated/0", "/sdcard", "/storage/self/primary" };
            foreach (string root in primaryRoots)
            {
                if (path == root)
                    return "primary:";
                if (path.StartsWith(root + "/", StringComparison.Ordinal))
                    return "primary:" + path.Substring(root.Length + 1);
            }

            const string storagePrefix = "/storage/";
            if (path.StartsWith(storagePrefix, StringComparison.Ordinal))
            {
                string[] parts = path.Substring(storagePrefix.Length).Split('/', 2);
                if (parts.Length > 0 && parts[0].Length > 0)
                    return parts[0] + ":" + (parts.Length > 1 ? parts[1] : string.Empty);
            }
            return null;
        }

        private global::Android.Content.Context CreateDialogContext()
        {
            return new ContextThemeWrapper(this, global::Android.Resource.Style.ThemeDeviceDefaultDialogAlert);
        }

        private void ShowOverlayDialog(AlertDialog dialog)
        {
            try
            {
                dialog.Window?.SetType(WindowManagerTypes.AccessibilityOverlay);
                dialog.Show();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show the dialog: " + ex);
            }
        }

        private void ShowAddAppDialog()
        {
            try
            {
                var entries = QueryLauncherApps();
                if (entries.Count == 0)
                    return;

                var builder = new AlertDialog.Builder(CreateDialogContext());
                builder.SetTitle("+ App");
                builder.SetItems(entries.Select(x => x.Label).ToArray(), (s, e) =>
                {
                    if (e.Which < 0 || e.Which >= entries.Count)
                        return;
                    var picked = entries[e.Which];
                    AddShortcut(ShortcutTypeApp, picked.Package, picked.Label);
                    RebuildMenuOverlay();
                });
                builder.SetNegativeButton(global::Android.Resource.String.Cancel, (s, e) => { });
                ShowOverlayDialog(builder.Create());
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show the add app dialog: " + ex);
            }
        }

        private void ShowAddFolderDialog()
        {
            try
            {
                Context dialogContext = CreateDialogContext();
                float density = Resources.DisplayMetrics.Density;

                var input = new EditText(dialogContext)
                {
                    Text = DefaultFolderPath,
                };
                input.SetSingleLine(true);
                input.SetSelection(input.Text.Length);

                var container = new FrameLayout(dialogContext);
                int pad = (int)(20 * density);
                container.SetPadding(pad, pad / 2, pad, 0);
                container.AddView(input, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

                var builder = new AlertDialog.Builder(dialogContext);
                builder.SetTitle("+ Folder");
                builder.SetView(container);
                builder.SetPositiveButton(global::Android.Resource.String.Ok, (s, e) =>
                {
                    string path = input.Text?.Trim();
                    if (string.IsNullOrEmpty(path))
                        return;
                    if (path.Length > 1)
                        path = path.TrimEnd('/');

                    string label = path.Substring(path.LastIndexOf('/') + 1);
                    if (string.IsNullOrWhiteSpace(label))
                        label = path;
                    AddShortcut(ShortcutTypeFolder, path, label);
                    RebuildMenuOverlay();
                });
                builder.SetNegativeButton(global::Android.Resource.String.Cancel, (s, e) => { });
                ShowOverlayDialog(builder.Create());
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not show the add folder dialog: " + ex);
            }
        }

        private bool ShouldShowFloatingButton()
        {
            return LoadSettings()?.ShowFloatingButton ?? false;
        }

        private bool ShouldCaptureBrowserUrl()
        {
            return LoadSettings()?.BookyUrlCaptureEnabled ?? false;
        }

        private bool ShouldMonitorClipboard()
        {
            return LoadSettings()?.ClipboardMonitoringEnabled ?? false;
        }

        internal SettingsModel LoadSettings()
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
        /// Starts a full-screen or partial screenshot. Closes the floating menu first and hides the
        /// floating button until the capture finishes.
        /// </summary>
        internal void StartScreenshot(bool partial)
        {
            _screenshotCapture ??= new ScreenshotCapture(this);
            _screenshotCapture.Start(partial);
        }

        /// <summary>
        /// Toggles screen recording via MainActivity (MediaProjection consent needs an activity).
        /// </summary>
        private void ToggleScreenRecording()
        {
            try
            {
                if (ScreenRecordService.IsRecording)
                {
                    ScreenRecordService.RequestStop(this);
                    Toast.MakeText(this, "Screen recording stopped", ToastLength.Short)?.Show();
                    return;
                }

                Intent intent = new Intent(this, typeof(MainActivity));
                intent.PutExtra(ExtraOpenTab, 9);
                intent.PutExtra(ExtraToggleScreenRecord, true);
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Toggle screen recording failed: " + ex);
            }
        }

        /// <summary>
        /// Temporarily hides the floating button so it is not included in screenshots.
        /// When un-hiding, recreates the overlay if it was lost (e.g. service rebound during capture).
        /// </summary>
        internal void SetFloatingButtonTemporarilyHidden(bool hidden)
        {
            try
            {
                if (hidden)
                {
                    if (_overlayView != null)
                        _overlayView.Visibility = ViewStates.Gone;
                    return;
                }

                if (!ShouldShowFloatingButton())
                {
                    if (_overlayView != null)
                        _overlayView.Visibility = ViewStates.Gone;
                    return;
                }

                // Recreate when the overlay was removed while the screenshot ran.
                if (_overlayView == null)
                {
                    ShowOverlay();
                    return;
                }

                _overlayView.Visibility = ViewStates.Visible;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not restore floating button visibility: " + ex);
                try
                {
                    if (!hidden && ShouldShowFloatingButton())
                        ShowOverlay();
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Exposes the service provider for screenshot localization without widening other APIs.
        /// </summary>
        internal IServiceProvider GetServiceProviderForScreenshot()
        {
            return GetServiceProvider();
        }

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

        private void OpenApplication()
        {
            Intent intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }

        internal void OpenApplicationAtTab(int tabIndex)
        {
            Intent intent = new Intent(this, typeof(MainActivity));
            intent.PutExtra(ExtraOpenTab, tabIndex);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
            StartActivity(intent);
        }

        private void OpenSystemPage()
        {
            // Set pending route in-process first: the accessibility service shares the app process,
            // so Blazor can navigate even if the Intent extra is dropped on some OEM launches.
            // Do not send RouteIntentMessage here — the WebView may still be paused; MainLayout
            // navigates from AfterResume / OnNewIntent instead.
            string route = RouteNames.System;
            RouteIntentRequest.Pending = new RouteIntentRequest { Route = route };

            Intent intent = new Intent(this, typeof(MainActivity));
            intent.PutExtra(ExtraOpenRoute, route);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ReorderToFront | ActivityFlags.ClearTop);
            StartActivity(intent);
        }

        private void OpenAbroBrowser()
        {
            try
            {
                var intent = new Intent(this, typeof(AbroBrowserActivity));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ReorderToFront | ActivityFlags.SingleTop);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not open Abro: " + ex);
                OpenApplicationAtTab(AbroViewModel.TabIndex);
            }
        }

        private void OpenVoiceCapture()
        {
            try
            {
                var intent = new Intent(this, typeof(VoiceCaptureActivity));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ReorderToFront | ActivityFlags.SingleTop);
                StartActivity(intent);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Could not open Voice capture: " + ex);
                OpenApplicationAtTab(VoiceViewModel.TabIndex);
            }
        }

        /// <summary>
        /// Handles single tap (popup menu), double click, long press, directional swipes
        /// (up/down/left/right), and dragging of the floating button.
        /// </summary>
        private class OverlayTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
        {
            private readonly FloatingButtonAccessibilityService _service;
            private float _touchStartRawX;
            private float _touchStartRawY;
            private int _paramsStartX;
            private int _paramsStartY;
            private long _downTimestampMs;
            private bool _isDragging;
            private bool _longPressFired;
            private System.Action _longPressAction;
            private System.Action _singleTapAction;
            private long _lastTapUpTimeMs;
            private float _touchSlop;

            public OverlayTouchListener(FloatingButtonAccessibilityService service)
            {
                _service = service;
            }

            private float GetTouchSlop(global::Android.Views.View view)
            {
                if (_touchSlop <= 0f)
                    _touchSlop = global::Android.Views.ViewConfiguration.Get(view.Context).ScaledTouchSlop * 2f;
                return _touchSlop;
            }

            public bool OnTouch(global::Android.Views.View view, MotionEvent motionEvent)
            {
                switch (motionEvent.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _touchStartRawX = motionEvent.RawX;
                        _touchStartRawY = motionEvent.RawY;
                        _paramsStartX = _service._overlayParams.X;
                        _paramsStartY = _service._overlayParams.Y;
                        _downTimestampMs = SystemClock.UptimeMillis();
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
                            return true;

                        if (_isDragging)
                        {
                            float totalDx = motionEvent.RawX - _touchStartRawX;
                            float totalDy = motionEvent.RawY - _touchStartRawY;
                            long durationMs = SystemClock.UptimeMillis() - _downTimestampMs;

                            if (TryHandleSwipeGesture(view, totalDx, totalDy, durationMs))
                                return true;

                            var preferences = _service.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
                            preferences.Edit()
                                .PutInt(PreferenceKeyX, _service._overlayParams.X)
                                .PutInt(PreferenceKeyY, _service._overlayParams.Y)
                                .Apply();
                        }
                        else
                        {
                            HandleTap();
                        }
                        return true;

                    case MotionEventActions.Cancel:
                        CancelLongPress();
                        return true;

                    default:
                        return true;
                }
            }

            private bool TryHandleSwipeGesture(global::Android.Views.View view, float dx, float dy, long durationMs)
            {
                if (durationMs > SwipeMaxDurationMs)
                    return false;

                float density = _service.Resources.DisplayMetrics.Density;
                float minSwipeDistance = 28f * density;
                float absX = Math.Abs(dx);
                float absY = Math.Abs(dy);
                if (Math.Max(absX, absY) < minSwipeDistance)
                    return false;

                SettingsModel settings = _service.LoadSettings();
                if (settings == null)
                    return false;

                string configuredAction = null;
                if (absX >= absY)
                {
                    configuredAction = dx > 0
                        ? settings.FloatingGestureSwipeRight
                        : settings.FloatingGestureSwipeLeft;
                }
                else
                {
                    configuredAction = dy > 0
                        ? settings.FloatingGestureSwipeDown
                        : settings.FloatingGestureSwipeUp;
                }

                if (string.IsNullOrWhiteSpace(configuredAction) ||
                    string.Equals(configuredAction, ":none", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Snap the floating button back to its position before the swipe gesture
                _service._overlayParams.X = _paramsStartX;
                _service._overlayParams.Y = _paramsStartY;
                _service._windowManager.UpdateViewLayout(view, _service._overlayParams);

                return _service.ExecuteConfiguredGesture(configuredAction);
            }

            private void HandleTap()
            {
                SettingsModel settings = _service.LoadSettings();
                string doubleClickAction = settings?.FloatingGestureDoubleClick;
                bool hasDoubleClick = !string.IsNullOrWhiteSpace(doubleClickAction) &&
                    !string.Equals(doubleClickAction, ":none", StringComparison.OrdinalIgnoreCase);

                if (!hasDoubleClick)
                {
                    _service.ToggleMenuOverlay();
                    return;
                }

                long now = SystemClock.UptimeMillis();
                if ((now - _lastTapUpTimeMs) <= DoubleTapTimeoutMs && _singleTapAction != null)
                {
                    CancelSingleTap();
                    _lastTapUpTimeMs = 0;
                    _service.ExecuteConfiguredGesture(doubleClickAction);
                    return;
                }

                _lastTapUpTimeMs = now;
                CancelSingleTap();
                _singleTapAction = () =>
                {
                    _singleTapAction = null;
                    _service.ToggleMenuOverlay();
                };
                _service._gestureHandler?.PostDelayed(_singleTapAction, DoubleTapTimeoutMs);
            }

            private void ScheduleLongPress()
            {
                CancelLongPress();
                _longPressAction = () =>
                {
                    _longPressFired = true;
                    SettingsModel settings = _service.LoadSettings();
                    string longPressAction = settings?.FloatingGestureLongPress;
                    if (string.IsNullOrWhiteSpace(longPressAction))
                        longPressAction = ":capture_text";
                    _service.ExecuteConfiguredGesture(longPressAction);
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

            private void CancelSingleTap()
            {
                if (_singleTapAction != null)
                {
                    _service._gestureHandler?.RemoveCallbacks(_singleTapAction);
                    _singleTapAction = null;
                }
            }
        }
    }
}
