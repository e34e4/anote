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
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using SilentNotes.Services;
using SilentNotes.Workers;
using Color = global::Android.Graphics.Color;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Top-centered teal/status banner. Avoids Android Toast (gravity ignored on API 30+,
    /// default gray bottom look) and draws a WindowManager / activity overlay instead.
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
        private static View _activeBanner;
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
            Context appCtx = context.ApplicationContext ?? context;

            void Run()
            {
                try
                {
                    // Prefer an activity content overlay when the UI is visible.
                    Activity activity = context as Activity
                        ?? (context is ContextWrapper cw ? FindActivity(cw) : null);
                    if (activity != null && !activity.IsFinishing && !activity.IsDestroyed)
                    {
                        if (ShowOnActivity(activity, message, bg, fg, durationMs))
                            return;
                    }

                    // Accessibility service: TYPE_ACCESSIBILITY_OVERLAY (no user overlay grant).
                    if (context is AccessibilityService a11y)
                    {
                        if (ShowViaWindowManager(a11y, message, bg, fg, durationMs, WindowManagerTypes.AccessibilityOverlay))
                            return;
                    }

                    // App overlay permission (same as floating button).
                    if (ShowViaWindowManager(appCtx, message, bg, fg, durationMs, WindowManagerTypes.ApplicationOverlay))
                        return;

                    // Last resort: still avoid default bottom Toast — try Phone type on older APIs.
                    if (Build.VERSION.SdkInt < BuildVersionCodes.O
                        && ShowViaWindowManager(appCtx, message, bg, fg, durationMs, WindowManagerTypes.Phone))
                        return;

                    AnoteFileLog.Append("toast", "Overlay failed; muted fallback for: " + message);
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
            _activeBanner = banner;
            _activeWm = null;

            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                try { root.RemoveView(banner); } catch (Exception) { }
                if (ReferenceEquals(_activeBanner, banner))
                    _activeBanner = null;
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
                var wm = context.GetSystemService(Context.WindowService) as IWindowManager;
                if (wm == null)
                    return false;

                float density = context.Resources?.DisplayMetrics?.Density ?? 1f;
                TextView banner = BuildBanner(context, message, bg, fg, density);

                var lp = new WindowManagerLayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    ViewGroup.LayoutParams.WrapContent,
                    type,
                    WindowManagerFlags.NotFocusable
                        | WindowManagerFlags.NotTouchable
                        | WindowManagerFlags.LayoutInScreen
                        | WindowManagerFlags.LayoutNoLimits,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal,
                    Y = Dp(density, 48),
                    Title = "aNoteToast",
                    WindowAnimations = 0,
                };

                DismissActive();
                wm.AddView(banner, lp);
                _activeBanner = banner;
                _activeWm = wm;

                Context captured = context;
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    try { wm.RemoveView(banner); } catch (Exception) { }
                    if (ReferenceEquals(_activeBanner, banner))
                    {
                        _activeBanner = null;
                        _activeWm = null;
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
