// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Appwidget;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using SilentNotes.Platforms;
using AView = Android.Views.View;
using AButton = Android.Widget.Button;
using AColor = Android.Graphics.Color;

namespace SilentNotes
{
    /// <summary>
    /// Black / green typing pad for the home-screen widget. Saves into the pinned "widget" note
    /// without opening the main aNote UI. Shows an Open button after the field is focused.
    /// </summary>
    [Activity(
        Label = "aNote widget",
        Theme = "@android:style/Theme.Black.NoTitleBar",
        Exported = false,
        ExcludeFromRecents = true,
        LaunchMode = LaunchMode.SingleTop,
        WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateVisible)]
    public class WidgetInputActivity : Activity
    {
        private EditText _edit;
        private AButton _openButton;
        private Handler _handler;
        private Java.Lang.IRunnable _saveRunnable;
        private bool _dirty;
        private int _appWidgetId = AppWidgetManager.InvalidAppwidgetId;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _appWidgetId = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId)
                ?? AppWidgetManager.InvalidAppwidgetId;

            _handler = new Handler(Looper.MainLooper);
            _saveRunnable = new Java.Lang.Runnable(SaveNow);

            Window?.SetStatusBarColor(AColor.Black);
            Window?.SetNavigationBarColor(AColor.Black);

            var root = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            root.SetBackgroundColor(AColor.Black);
            int pad = Dp(12);
            root.SetPadding(pad, pad, pad, pad);

            _edit = new EditText(this)
            {
                Gravity = GravityFlags.Top | GravityFlags.Start,
                InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine | InputTypes.TextFlagCapSentences,
                Hint = "Type here…",
            };
            _edit.SetTextColor(AColor.ParseColor("#00FF00"));
            _edit.SetHintTextColor(AColor.ParseColor("#006600"));
            _edit.SetBackgroundColor(AColor.Transparent);
            _edit.Typeface = Android.Graphics.Typeface.Monospace;
            _edit.TextSize = 16f;
            _edit.SetMinLines(8);
            _edit.Text = WidgetNotePersist.LoadText(this);
            _edit.SetSelection(_edit.Text?.Length ?? 0);
            _edit.TextChanged += OnTextChanged;
            _edit.FocusChange += OnFocusChange;
            _edit.Click += (s, e) => ShowOpenButton();

            _openButton = new AButton(this)
            {
                Text = "Open in aNote",
                Visibility = ViewStates.Gone,
            };
            _openButton.SetBackgroundColor(AColor.ParseColor("#111111"));
            _openButton.SetTextColor(AColor.ParseColor("#00FF00"));
            _openButton.Typeface = Android.Graphics.Typeface.Monospace;
            _openButton.Click += (s, e) =>
            {
                SaveNow();
                WidgetAppWidgetProvider.OpenWidgetNote(this);
                Finish();
            };

            root.AddView(_edit, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));
            root.AddView(_openButton, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            SetContentView(root);

            // First interaction surface: reveal Open when the pad is shown.
            ShowOpenButton();
            _edit.RequestFocus();
        }

        protected override void OnPause()
        {
            SaveNow();
            WidgetAppWidgetProvider.RefreshAll(this);
            base.OnPause();
        }

        protected override void OnDestroy()
        {
            try
            {
                _handler?.RemoveCallbacks(_saveRunnable);
            }
            catch
            {
            }

            base.OnDestroy();
        }

        public override void OnBackPressed()
        {
            SaveNow();
            WidgetAppWidgetProvider.RefreshAll(this);
            base.OnBackPressed();
        }

        private void OnTextChanged(object sender, global::Android.Text.TextChangedEventArgs e)
        {
            _dirty = true;
            _handler.RemoveCallbacks(_saveRunnable);
            _handler.PostDelayed(_saveRunnable, 400);
            ShowOpenButton();
        }

        private void OnFocusChange(object sender, AView.FocusChangeEventArgs e)
        {
            if (e.HasFocus)
                ShowOpenButton();
        }

        private void ShowOpenButton()
        {
            if (_openButton != null)
                _openButton.Visibility = ViewStates.Visible;
        }

        private void SaveNow()
        {
            if (!_dirty && _edit == null)
                return;

            string text = _edit?.Text ?? string.Empty;
            if (WidgetNotePersist.SaveText(this, text))
                _dirty = false;

            if (_appWidgetId != AppWidgetManager.InvalidAppwidgetId)
            {
                try
                {
                    AppWidgetManager manager = AppWidgetManager.GetInstance(this);
                    var views = new RemoteViews(PackageName, Resource.Layout.widget_note);
                    views.SetTextViewText(Resource.Id.widget_text, text);
                    views.SetViewVisibility(Resource.Id.widget_actions, ViewStates.Visible);
                    manager?.PartiallyUpdateAppWidget(_appWidgetId, views);
                }
                catch
                {
                }
            }
        }

        private int Dp(int value)
        {
            return (int)(value * Resources.DisplayMetrics.Density + 0.5f);
        }
    }
}
