// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Services;
using SilentNotes.Workers;
using Color = global::Android.Graphics.Color;
using AView = Android.Views.View;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Top-centered teal/status banner. Avoids Android Toast (gravity ignored on API 30+,
    /// default gray bottom look; ColorOS also shows a separate "aNote pasted…" system toast).
    /// Draws via the accessibility WindowManager when available (same path as the floating button).
    /// </summary>
    internal static class AnoteToast
    {
        private static readonly Color TealBg = Color.ParseColor("#1F6F6A");
        private static readonly Color TealFg = Color.ParseColor("#F4FFFD");
        private static readonly Color InfoBg = Color.ParseColor("#1F5F9A");
        private static readonly Color InfoFg = Color.ParseColor("#F2F8FF");
        private static readonly Color SuccessBg = Color.ParseColor("#2A7A36");
        private static readonly Color SuccessFg = Color.ParseColor("#F3FFF4");
        private static readonly Color WarningBg = Color.ParseColor("#B86A0C");
        private static readonly Color WarningFg = Color.ParseColor("#FFF8EF");
        private static readonly Color ErrorBg = Color.ParseColor("#B32121");
        private static readonly Color ErrorFg = Color.ParseColor("#FFF5F5");

        private static readonly object Gate = new object();
        private static AView _activeBanner;
        private static IWindowManager _activeWm;

        public static void Show(
            Context context,
            string message,
            FeedbackSeverity severity = FeedbackSeverity.Unknown,
            ToastLength length = ToastLength.Short)
        {
            if (context == null || string.IsNullOrWhiteSpace(message))
                return;

            int durationMs = length == ToastLength.Long ? 3500 : 2200;
            (Color bg, Color fg) = ColorsFor(severity);

            void Run()
            {
                try
                {
                    // 1) Accessibility service WM — proven to work for the floating button.
                    FloatingButtonAccessibilityService a11y = FloatingButtonAccessibilityService.Instance
                        ?? (context as FloatingButtonAccessibilityService);
                    if (a11y != null
                        && ShowViaWindowManager(a11y, message, bg, fg, durationMs, WindowManagerTypes.AccessibilityOverlay))
                        return;

                    if (context is AccessibilityService otherA11y
                        && ShowViaWindowManager(otherA11y, message, bg, fg, durationMs, WindowManagerTypes.AccessibilityOverlay))
                        return;

                    // 2) Activity content overlay when the UI is visible.
                    Activity activity = context as Activity
                        ?? (context is ContextWrapper cw ? FindActivity(cw) : null)
                        ?? TryGetRootActivity();
                    if (activity != null && !activity.IsFinishing && !activity.IsDestroyed)
                    {
                        if (ShowOnActivity(activity, message, bg, fg, durationMs))
                            return;
                    }

                    // 3) App overlay permission (same as SYSTEM_ALERT_WINDOW).
                    Context appCtx = context.ApplicationContext ?? context;
                    if (ShowViaWindowManager(appCtx, message, bg, fg, durationMs, WindowManagerTypes.ApplicationOverlay))
                        return;

                    if (Build.VERSION.SdkInt < BuildVersionCodes.O
                        && ShowViaWindowManager(appCtx, message, bg, fg, durationMs, WindowManagerTypes.Phone))
                        return;

                    AnoteFileLog.Append("toast", "Overlay failed; no banner for: " + message);
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Append("toast", "Show failed: " + ex.Message);
                }
            }

            Looper main = Looper.MainLooper;
            if (main != null && Looper.MyLooper() != main)
                new Handler(main).Post(Run);
            else
                Run();
        }

        private static Activity TryGetRootActivity()
        {
            try
            {
                return Platform.CurrentActivity;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Activity FindActivity(ContextWrapper wrapper)
        {
            Context c = wrapper;
            for (int i = 0; i < 6 && c != null; i++)
            {
                if (c is Activity a)
                    return a;
                c = (c as ContextWrapper)?.BaseContext;
            }

            return null;
        }

        private static bool ShowOnActivity(Activity activity, string message, Color bg, Color fg, int durationMs)
        {
            ViewGroup root = activity.FindViewById(global::Android.Resource.Id.Content) as ViewGroup;
            if (root == null)
                return false;

            float density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            TextView banner = BuildBanner(activity, message, bg, fg, density);
            var lp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent,
                GravityFlags.Top | GravityFlags.CenterHorizontal)
            {
                TopMargin = Dp(density, 48),
                LeftMargin = Dp(density, 8),
                RightMargin = Dp(density, 8),
            };

            DismissActive();
            root.AddView(banner, lp);
            lock (Gate)
            {
                _activeBanner = banner;
                _activeWm = null;
            }

            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                try { root.RemoveView(banner); } catch (Exception) { }
                lock (Gate)
                {
                    if (ReferenceEquals(_activeBanner, banner))
                        _activeBanner = null;
                }
            }, durationMs);
            return true;
        }

        private static bool ShowViaWindowManager(
            Context context,
            string message,
            Color bg,
            Color fg,
            int durationMs,
            WindowManagerTypes type)
        {
            try
            {
                IWindowManager wm = ResolveWindowManager(context);
                if (wm == null)
                {
                    AnoteFileLog.Append("toast", "WindowManager null for " + type);
                    return false;
                }

                float density = context.Resources?.DisplayMetrics?.Density ?? 1f;
                TextView banner = BuildBanner(context, message, bg, fg, density);

                // Match floating-button flags (LayoutInScreen/NoLimits caused OEM AddView failures).
                var lp = new WindowManagerLayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    ViewGroup.LayoutParams.WrapContent,
                    type,
                    WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal,
                    Y = Dp(density, 48),
                    Title = "aNoteToast",
                    WindowAnimations = 0,
                };

                DismissActive();
                wm.AddView(banner, lp);
                lock (Gate)
                {
                    _activeBanner = banner;
                    _activeWm = wm;
                }

                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    try { wm.RemoveView(banner); } catch (Exception) { }
                    lock (Gate)
                    {
                        if (ReferenceEquals(_activeBanner, banner))
                        {
                            _activeBanner = null;
                            _activeWm = null;
                        }
                    }
                }, durationMs);
                return true;
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("toast", "WindowManager " + type + " failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Same cast path as <see cref="FloatingButtonAccessibilityService"/> — soft
        /// <c>as IWindowManager</c> often returns null on MAUI/Android proxies.
        /// </summary>
        private static IWindowManager ResolveWindowManager(Context context)
        {
            try
            {
                Java.Lang.Object systemService = context.GetSystemService(Context.WindowService);
                if (systemService == null)
                    return null;
                return systemService.JavaCast<IWindowManager>();
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("toast", "ResolveWindowManager failed: " + ex.Message);
                return null;
            }
        }

        private static void DismissActive()
        {
            lock (Gate)
            {
                try
                {
                    if (_activeBanner != null && _activeWm != null)
                        _activeWm.RemoveView(_activeBanner);
                }
                catch (Exception)
                {
                }

                _activeBanner = null;
                _activeWm = null;
            }
        }

        private static TextView BuildBanner(Context context, string message, Color bg, Color fg, float density)
        {
            var text = new TextView(context);
            text.Text = message;
            text.SetTextColor(fg);
            text.SetTextSize(ComplexUnitType.Sp, 13f);
            text.SetPadding(Dp(density, 14), Dp(density, 8), Dp(density, 14), Dp(density, 8));
            text.SetMaxLines(4);
            text.Ellipsize = TextUtils.TruncateAt.End;
            text.Gravity = GravityFlags.Center;

            var shape = new GradientDrawable();
            shape.SetColor(bg);
            shape.SetCornerRadius(Dp(density, 10));
            text.Background = shape;
            text.Elevation = 12f;
            return text;
        }

        private static (Color Bg, Color Fg) ColorsFor(FeedbackSeverity severity)
        {
            return severity switch
            {
                FeedbackSeverity.Info => (InfoBg, InfoFg),
                FeedbackSeverity.Success => (SuccessBg, SuccessFg),
                FeedbackSeverity.Warning => (WarningBg, WarningFg),
                FeedbackSeverity.Error => (ErrorBg, ErrorFg),
                _ => (TealBg, TealFg),
            };
        }

        private static int Dp(float density, int dp) => (int)(dp * density + 0.5f);
    }
}
