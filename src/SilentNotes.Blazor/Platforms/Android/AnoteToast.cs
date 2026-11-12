// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using SilentNotes.Services;
using Color = Android.Graphics.Color;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Top-centered status banner styled like MudBlazor snackbars (custom Toast views are blocked
    /// on Android 11+). Falls back to a gravity-top text toast when no window is available.
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

            try
            {
                Activity activity = context as Activity;
                if (activity != null && !activity.IsFinishing)
                {
                    activity.RunOnUiThread(() => ShowOnActivity(activity, message, bg, fg, durationMs));
                    return;
                }

                if (TryShowViaWindowManager(context, message, bg, fg, durationMs))
                    return;
            }
            catch (Exception)
            {
            }

            try
            {
                Toast toast = Toast.MakeText(context.ApplicationContext ?? context, message, length);
#pragma warning disable CS0618
                toast?.SetGravity(GravityFlags.Top | GravityFlags.CenterHorizontal, 0, 48);
#pragma warning restore CS0618
                toast?.Show();
            }
            catch (Exception)
            {
            }
        }

        private static void ShowOnActivity(Activity activity, string message, Color bg, Color fg, int durationMs)
        {
            ViewGroup root = activity.FindViewById(Android.Resource.Id.Content) as ViewGroup;
            if (root == null)
            {
                Toast.MakeText(activity, message, ToastLength.Short)?.Show();
                return;
            }

            float density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            TextView banner = BuildBanner(activity, message, bg, fg, density);
            var lp = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent,
                GravityFlags.Top | GravityFlags.CenterHorizontal)
            {
                TopMargin = Dp(density, 10),
                LeftMargin = Dp(density, 8),
                RightMargin = Dp(density, 8),
            };

            root.AddView(banner, lp);
            var handler = new Handler(Looper.MainLooper);
            handler.PostDelayed(() =>
            {
                try
                {
                    root.RemoveView(banner);
                }
                catch (Exception)
                {
                }
            }, durationMs);
        }

        private static bool TryShowViaWindowManager(
            Context context,
            string message,
            Color bg,
            Color fg,
            int durationMs)
        {
            try
            {
                var wm = context.GetSystemService(Context.WindowService) as IWindowManager;
                if (wm == null)
                    return false;

                float density = context.Resources?.DisplayMetrics?.Density ?? 1f;
                TextView banner = BuildBanner(context, message, bg, fg, density);

                var type = Build.VERSION.SdkInt >= BuildVersionCodes.O
                    ? WindowManagerTypes.ApplicationOverlay
                    : WindowManagerTypes.Phone;

                // Accessibility services can use TYPE_ACCESSIBILITY_OVERLAY without the user
                // overlay permission when the service is enabled.
                if (context is AccessibilityService)
                    type = WindowManagerTypes.AccessibilityOverlay;

                var lp = new WindowManagerLayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    ViewGroup.LayoutParams.WrapContent,
                    type,
                    WindowManagerFlags.NotFocusable
                        | WindowManagerFlags.NotTouchable
                        | WindowManagerFlags.KeepScreenOn,
                    Format.Translucent)
                {
                    Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal,
                    Y = Dp(density, 10),
                    Title = "aNoteToast",
                };

                wm.AddView(banner, lp);
                var handler = new Handler(Looper.MainLooper);
                handler.PostDelayed(() =>
                {
                    try
                    {
                        wm.RemoveView(banner);
                    }
                    catch (Exception)
                    {
                    }
                }, durationMs);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static TextView BuildBanner(Context context, string message, Color bg, Color fg, float density)
        {
            var text = new TextView(context);
            text.Text = message;
            text.SetTextColor(fg);
            text.SetTextSize(ComplexUnitType.Sp, 12.5f);
            text.SetPadding(Dp(density, 10), Dp(density, 5), Dp(density, 10), Dp(density, 5));
            text.SetMaxLines(4);
            text.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            text.Gravity = GravityFlags.Center;

            try
            {
                var shape = new Android.Graphics.Drawables.GradientDrawable();
                shape.SetColor(bg);
                shape.SetCornerRadius(Dp(density, 8));
                text.Background = shape;
            }
            catch (Exception)
            {
                text.SetBackgroundColor(bg);
            }

            text.Elevation = 8f;
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
