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
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
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
using SilentNotes.Workers;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;

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

        private const string PreferencesName = "anote_floating_button";
        private const string PreferenceKeyX = "x";
        private const string PreferenceKeyY = "y";
        private const string PreferenceKeyMenuX = "menu_x";
        private const string PreferenceKeyMenuY = "menu_y";
        private const string PreferenceKeyMenuWidth = "menu_w";
        private const string PreferenceKeyMenuHeight = "menu_h";
        private const string LogTag = "aNoteFloating";
        private const int LongPressDelayMs = 600;
        private const int DoubleTapTimeoutMs = 260;
        private const int SwipeMaxDurationMs = 480;
        private const int SelectionMaxAgeSeconds = 120;

        private static readonly (int Index, string Icon, string Key, string Fallback)[] TabDefinitions = new[]
        {
            (0, "📝", "tab_notes", "Notes"),
            (1, "📋", "tab_clipboard", "Clip"),
            (2, "🔖", "tab_booky", "Booky"),
            (3, "👤", "tab_phone", "Contacts"),
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
        };

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

        /// <inheritdoc/>
        public override void OnAccessibilityEvent(AccessibilityEvent e)
        {
            if (e == null)
                return;

            RememberTextSelection(e);

            if (!_captureEnabled)
                return;

            string packageName = e.PackageName?.ToString();
            if (string.IsNullOrEmpty(packageName) || !BrowserPackages.Contains(packageName))
                return;

            RefreshFromPackage(packageName);
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

                // Header row (draggable): Title + Reset Size (½H) + Close button
                var headerRow = new LinearLayout(this)
                {
                    Orientation = Orientation.Horizontal,
                };
                headerRow.SetGravity(GravityFlags.CenterVertical);
                headerRow.SetPadding((int)(4 * density), (int)(2 * density), (int)(4 * density), (int)(6 * density));

                var titleView = new TextView(this)
                {
                    Text = "⠿ aNote Quick Menu",
                    TextSize = 13.5f,
                };
                titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                titleView.SetTextColor(Color.White);
                headerRow.AddView(titleView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

                var halfBtn = new TextView(this)
                {
                    Text = "½",
                    TextSize = 12f,
                    Gravity = GravityFlags.Center,
                };
                halfBtn.SetTextColor(Color.ParseColor("#38BDF8"));
                int btnPad = (int)(6 * density);
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

                // Scrollable grid of 14 tab buttons + 1 Keyboard Switcher button (4 columns)
                var scrollView = new global::Android.Widget.ScrollView(this)
                {
                    FillViewport = true,
                };
                var gridContainer = new LinearLayout(this)
                {
                    Orientation = Orientation.Vertical,
                };

                const int columns = 4;
                var allButtons = new List<(string Icon, string Label, System.Action OnClick, bool IsAccent)>();

                foreach (var tab in TabDefinitions)
                {
                    int tabIdx = tab.Index;
                    string label = language?[tab.Key];
                    if (string.IsNullOrWhiteSpace(label) || label == tab.Key)
                        label = tab.Fallback;
                    allButtons.Add((tab.Icon, label, () =>
                    {
                        HideMenuOverlay();
                        OpenApplicationAtTab(tabIdx);
                    }, false));
                }

                // Keyboard Switcher button (Kunzisoft KeyboardSwitcher functionality)
                string kbLabel = language?["floating_menu_keyboard"];
                if (string.IsNullOrWhiteSpace(kbLabel) || kbLabel == "floating_menu_keyboard")
                    kbLabel = "Keyboard";
                allButtons.Add(("⌨️", kbLabel, () =>
                {
                    HideMenuOverlay();
                    ShowKeyboardSwitcher();
                }, true));

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
                            global::Android.Views.View btnView = CreateMenuGridButton(item.Icon, item.Label, item.IsAccent, item.OnClick, density);
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

        private global::Android.Views.View CreateMenuGridButton(
            string icon,
            string label,
            bool isAccent,
            System.Action onClick,
            float density)
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

        private bool LaunchInstalledApp(string packageName)
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
                StartActivity(launchIntent);
                return true;
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(LogTag, "Failed to launch app " + packageName + ": " + ex);
                return false;
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
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
            StartActivity(intent);
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
