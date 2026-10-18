// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;

namespace SilentNotes
{
    /// <summary>
    /// Helper activity modeled after Kunzisoft's Android-KeyboardSwitcher
    /// (https://github.com/Kunzisoft/Android-KeyboardSwitcher).
    /// Because Android 9+ requires a focused foreground window before calling
    /// <see cref="InputMethodManager.ShowInputMethodPicker"/>, this lightweight dialog activity
    /// acquires window focus, triggers the system keyboard chooser dialog (and also shows installed
    /// keyboards + a shortcut to system keyboard settings), and closes automatically afterwards.
    /// </summary>
    [Activity(
        Label = "aNote Keyboard Switcher",
        Theme = "@android:style/Theme.DeviceDefault.Dialog.NoActionBar",
        Exported = false,
        ExcludeFromRecents = true,
        NoHistory = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class KeyboardSwitcherActivity : Activity
    {
        private Handler _handler;
        private bool _pickerRequested;
        private bool _dialogLostFocusAfterPicker;
        private float _density;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _density = Resources.DisplayMetrics.Density;
            _handler = new Handler(Looper.MainLooper);

            if (Window != null)
            {
                Window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
                Window.AddFlags(WindowManagerFlags.DimBehind);
                WindowManagerLayoutParams lp = Window.Attributes;
                if (lp != null)
                {
                    lp.DimAmount = 0.45f;
                    lp.Width = (int)(Resources.DisplayMetrics.WidthPixels * 0.88f);
                    lp.Height = ViewGroup.LayoutParams.WrapContent;
                    lp.Gravity = GravityFlags.Center;
                    Window.Attributes = lp;
                }
            }

            BuildFallbackAndInfoCard();
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            if (hasFocus && !_pickerRequested)
            {
                _pickerRequested = true;
                // Delay slightly so Android registers the window focus before opening the IME picker.
                _handler.PostDelayed(() =>
                {
                    try
                    {
                        var imm = GetSystemService(InputMethodService) as InputMethodManager;
                        imm?.ShowInputMethodPicker();
                    }
                    catch (Exception)
                    {
                    }
                }, 120);
            }
            else if (!hasFocus && _pickerRequested)
            {
                // The system IME picker dialog took focus on top of this activity.
                _dialogLostFocusAfterPicker = true;
            }
            else if (hasFocus && _dialogLostFocusAfterPicker)
            {
                // The user selected a keyboard or dismissed the system IME picker dialog.
                Finish();
            }
        }

        private void BuildFallbackAndInfoCard()
        {
            int pad = (int)(14 * _density);
            var card = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            card.SetPadding(pad, pad, pad, pad);

            var bg = new GradientDrawable();
            bg.SetColor(Color.ParseColor("#121212"));
            bg.SetCornerRadius(16 * _density);
            bg.SetStroke((int)(1 * _density), Color.ParseColor("#333333"));
            card.Background = bg;

            var header = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            header.SetGravity(GravityFlags.CenterVertical);

            var title = new TextView(this)
            {
                Text = "⌨️ Switch Keyboard",
                TextSize = 16f,
            };
            title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            title.SetTextColor(Color.White);
            header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            var closeBtn = CreateButton("✕", "#222222", "#FFFFFF");
            closeBtn.Click += (s, e) => Finish();
            header.AddView(closeBtn);
            card.AddView(header);

            var subTitle = new TextView(this)
            {
                Text = "Enabled keyboards on this device:",
                TextSize = 12f,
            };
            subTitle.SetTextColor(Color.ParseColor("#9E9E9E"));
            subTitle.SetPadding(0, (int)(8 * _density), 0, (int)(6 * _density));
            card.AddView(subTitle);

            var imm = GetSystemService(InputMethodService) as InputMethodManager;
            IList<InputMethodInfo> enabledImes = imm?.EnabledInputMethodList;
            if (enabledImes != null && enabledImes.Count > 0)
            {
                PackageManager pm = PackageManager;
                foreach (InputMethodInfo ime in enabledImes)
                {
                    string label = ime.LoadLabel(pm)?.ToString() ?? ime.PackageName;
                    var imeRow = new LinearLayout(this)
                    {
                        Orientation = Orientation.Horizontal,
                    };
                    imeRow.SetGravity(GravityFlags.CenterVertical);
                    imeRow.SetPadding((int)(10 * _density), (int)(8 * _density), (int)(10 * _density), (int)(8 * _density));

                    var rowBg = new GradientDrawable();
                    rowBg.SetColor(Color.ParseColor("#1C1C1C"));
                    rowBg.SetCornerRadius(10 * _density);
                    rowBg.SetStroke((int)(1 * _density), Color.ParseColor("#2C2C2C"));
                    imeRow.Background = rowBg;

                    var rowLp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                    rowLp.SetMargins(0, 0, 0, (int)(6 * _density));
                    imeRow.LayoutParameters = rowLp;

                    var nameTv = new TextView(this)
                    {
                        Text = label,
                        TextSize = 14f,
                    };
                    nameTv.SetTextColor(Color.White);
                    imeRow.AddView(nameTv, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

                    imeRow.Click += (s, e) =>
                    {
                        imm?.ShowInputMethodPicker();
                    };
                    card.AddView(imeRow);
                }
            }

            var actionsRow = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            actionsRow.SetPadding(0, (int)(8 * _density), 0, 0);

            var pickerBtn = CreateButton("⌨️ Choose Keyboard", "#1E293B", "#38BDF8");
            pickerBtn.Click += (s, e) =>
            {
                var mgr = GetSystemService(InputMethodService) as InputMethodManager;
                mgr?.ShowInputMethodPicker();
            };
            var p1 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            p1.SetMargins(0, 0, (int)(4 * _density), 0);
            actionsRow.AddView(pickerBtn, p1);

            var settingsBtn = CreateButton("⚙️ Manage Keyboards", "#1A1A1A", "#E5E5E5");
            settingsBtn.Click += (s, e) =>
            {
                try
                {
                    var intent = new Intent(Settings.ActionInputMethodSettings);
                    intent.AddFlags(ActivityFlags.NewTask);
                    StartActivity(intent);
                    Finish();
                }
                catch (Exception)
                {
                }
            };
            var p2 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            p2.SetMargins((int)(4 * _density), 0, 0, 0);
            actionsRow.AddView(settingsBtn, p2);

            card.AddView(actionsRow, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            SetContentView(card);
        }

        private TextView CreateButton(string text, string bgHex, string fgHex)
        {
            var tv = new TextView(this)
            {
                Text = text,
                TextSize = 13f,
                Gravity = GravityFlags.Center,
            };
            tv.SetTextColor(Color.ParseColor(fgHex));
            int hPad = (int)(12 * _density);
            int vPad = (int)(8 * _density);
            tv.SetPadding(hPad, vPad, hPad, vPad);

            var bg = new GradientDrawable();
            bg.SetColor(Color.ParseColor(bgHex));
            bg.SetCornerRadius(8 * _density);
            bg.SetStroke((int)(1 * _density), Color.ParseColor("#33FFFFFF"));
            tv.Background = bg;
            return tv;
        }
    }
}
