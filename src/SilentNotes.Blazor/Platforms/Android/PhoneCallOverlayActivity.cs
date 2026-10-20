// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using SilentNotes.Models;
using SilentNotes.Services;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;

namespace SilentNotes
{
    /// <summary>
    /// Compact floating call UI shown above other apps (under the status bar, about one third of
    /// the screen height) while an incoming or ongoing phone call is handled by aNote.
    /// </summary>
    [Activity(
        Label = "aNote Call",
        Theme = "@android:style/Theme.DeviceDefault.Dialog.NoActionBar",
        Exported = false,
        ExcludeFromRecents = true,
        LaunchMode = LaunchMode.SingleInstance,
        ShowWhenLocked = true,
        TurnScreenOn = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class PhoneCallOverlayActivity : Activity
    {
        private float _density;
        private TextView _titleView;
        private TextView _numberView;
        private TextView _stateView;
        private TextView _spamView;
        private TextView _answerBtn;
        private TextView _hangupBtn;
        private TextView _muteBtn;
        private TextView _speakerBtn;
        private bool _messengerRegistered;

        /// <summary>
        /// Shows or brings the floating call overlay to the front.
        /// </summary>
        public static void Show(Context context)
        {
            if (context == null)
                return;

            try
            {
                var intent = new Intent(context, typeof(PhoneCallOverlayActivity));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
                context.StartActivity(intent);
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _density = Resources.DisplayMetrics.Density;
            SetFinishOnTouchOutside(false);
            ConfigureFloatingWindow();
            BuildOverlayUi();
            RegisterCallChangedListener();
            RefreshCallUi();
        }

        /// <inheritdoc/>
        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            RefreshCallUi();
        }

        /// <inheritdoc/>
        protected override void OnResume()
        {
            base.OnResume();
            RefreshCallUi();
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            UnregisterCallChangedListener();
            base.OnDestroy();
        }

        private void ConfigureFloatingWindow()
        {
            if (Window == null)
                return;

            Window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
            Window.AddFlags(
                WindowManagerFlags.ShowWhenLocked |
                WindowManagerFlags.TurnScreenOn |
                WindowManagerFlags.KeepScreenOn |
                WindowManagerFlags.DimBehind);

            // Keep the panel interactive while letting the system draw the status bar above it.
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                Window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);
                Window.ClearFlags(WindowManagerFlags.TranslucentStatus);
            }

            WindowManagerLayoutParams lp = Window.Attributes;
            if (lp != null)
            {
                DisplayMetrics metrics = Resources.DisplayMetrics;
                lp.DimAmount = 0.2f;
                lp.Width = (int)(metrics.WidthPixels * 0.96f);
                lp.Height = (int)(metrics.HeightPixels / 3f);
                lp.Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal;
                lp.Y = GetStatusBarHeight();
                Window.Attributes = lp;
            }
        }

        private int GetStatusBarHeight()
        {
            try
            {
                int resourceId = Resources.GetIdentifier("status_bar_height", "dimen", "android");
                if (resourceId > 0)
                    return Resources.GetDimensionPixelSize(resourceId);
            }
            catch (Exception)
            {
            }
            return (int)(24 * _density);
        }

        private void BuildOverlayUi()
        {
            int pad = (int)(12 * _density);

            var card = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            card.SetPadding(pad, pad, pad, pad);
            var cardBg = new GradientDrawable();
            cardBg.SetColor(Color.ParseColor("#0E1A12"));
            cardBg.SetCornerRadius(16 * _density);
            cardBg.SetStroke((int)(1 * _density), Color.ParseColor("#2E7D32"));
            card.Background = cardBg;

            var headerRow = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            headerRow.SetGravity(GravityFlags.CenterVertical);

            var headerText = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };

            _titleView = new TextView(this)
            {
                TextSize = 18f,
                Text = "Phone call",
            };
            _titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            _titleView.SetTextColor(Color.White);
            headerText.AddView(_titleView);

            _numberView = new TextView(this)
            {
                TextSize = 13f,
                Visibility = ViewStates.Gone,
            };
            _numberView.SetTextColor(Color.ParseColor("#B0B0B0"));
            headerText.AddView(_numberView);

            headerRow.AddView(headerText, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            TextView expandBtn = CreateChip("Open", "#1A1A1A", "#E5E5E5");
            expandBtn.Click += (s, e) => OpenFullPhoneTab();
            headerRow.AddView(expandBtn);

            card.AddView(headerRow);

            _spamView = new TextView(this)
            {
                TextSize = 12.5f,
                Visibility = ViewStates.Gone,
            };
            _spamView.SetPadding(0, (int)(6 * _density), 0, 0);
            card.AddView(_spamView);

            _stateView = new TextView(this)
            {
                TextSize = 13.5f,
                Text = string.Empty,
            };
            _stateView.SetTextColor(Color.ParseColor("#9E9E9E"));
            _stateView.SetPadding(0, (int)(6 * _density), 0, (int)(10 * _density));
            card.AddView(_stateView);

            var actionsRow = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            actionsRow.SetGravity(GravityFlags.Center);

            _answerBtn = CreateChip(GetLabel("dialer_answer", "Answer"), "#1B5E20", "#FFFFFF");
            _answerBtn.Click += (s, e) =>
            {
                PhoneInCallService.AnswerCurrentCall();
                RefreshCallUi();
            };
            actionsRow.AddView(_answerBtn, CreateWeightedParams());

            _hangupBtn = CreateChip(GetLabel("dialer_hangup", "Hang up"), "#B71C1C", "#FFFFFF");
            _hangupBtn.Click += (s, e) =>
            {
                PhoneInCallService.HangupCurrentCall();
                RefreshCallUi();
            };
            actionsRow.AddView(_hangupBtn, CreateWeightedParams());

            card.AddView(actionsRow, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            var secondaryRow = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            secondaryRow.SetPadding(0, (int)(8 * _density), 0, 0);
            secondaryRow.SetGravity(GravityFlags.Center);

            _muteBtn = CreateChip(GetLabel("dialer_mute", "Mute"), "#1A1A1A", "#CCCCCC");
            _muteBtn.Click += (s, e) =>
            {
                PhoneInCallService.ToggleMuteCurrentCall();
                RefreshCallUi();
            };
            secondaryRow.AddView(_muteBtn, CreateWeightedParams());

            _speakerBtn = CreateChip(GetLabel("dialer_speaker", "Speaker"), "#1A1A1A", "#CCCCCC");
            _speakerBtn.Click += (s, e) =>
            {
                PhoneInCallService.ToggleSpeakerCurrentCall();
                RefreshCallUi();
            };
            secondaryRow.AddView(_speakerBtn, CreateWeightedParams());

            card.AddView(secondaryRow, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            SetContentView(card);
        }

        private void RefreshCallUi()
        {
            try
            {
                ActiveCallModel call = null;
                try
                {
                    call = Ioc.Instance.GetService<IDevicePhoneService>()?.GetActiveCall();
                }
                catch (Exception)
                {
                    call = PhoneInCallService.GetActiveCallSnapshot(null);
                }

                if (call == null)
                {
                    Finish();
                    return;
                }

                string title = string.IsNullOrWhiteSpace(call.DisplayName) ? call.Number : call.DisplayName;
                if (string.IsNullOrWhiteSpace(title))
                    title = "Phone call";
                _titleView.Text = title;

                if (!string.IsNullOrWhiteSpace(call.DisplayName) && !string.IsNullOrWhiteSpace(call.Number))
                {
                    _numberView.Text = call.Number;
                    _numberView.Visibility = ViewStates.Visible;
                }
                else
                {
                    _numberView.Visibility = ViewStates.Gone;
                }

                _stateView.Text = call.State ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(call.SpamSummary))
                {
                    _spamView.Text = call.SpamSummary;
                    _spamView.SetTextColor(Color.ParseColor(call.IsSpam ? "#EF9A9A" : "#A5D6A7"));
                    _spamView.Visibility = ViewStates.Visible;
                }
                else
                {
                    _spamView.Visibility = ViewStates.Gone;
                }

                _answerBtn.Visibility = call.IsRinging ? ViewStates.Visible : ViewStates.Gone;
                ApplyToggleStyle(_muteBtn, call.IsMuted);
                ApplyToggleStyle(_speakerBtn, call.IsSpeakerOn);
            }
            catch (Exception)
            {
            }
        }

        private void OpenFullPhoneTab()
        {
            try
            {
                PhoneIntentRequest.Pending = new PhoneIntentRequest();
                var intent = new Intent(this, typeof(MainActivity));
                intent.PutExtra(PhoneInCallService.ExtraOpenPhone, true);
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ReorderToFront);
                StartActivity(intent);
                Ioc.Instance.GetService<IMessengerService>()?.Send(new PhoneIntentMessage());
            }
            catch (Exception)
            {
            }
        }

        private void RegisterCallChangedListener()
        {
            if (_messengerRegistered)
                return;
            try
            {
                IMessengerService messenger = Ioc.Instance.GetService<IMessengerService>();
                if (messenger == null)
                    return;
                messenger.Register<PhoneCallChangedMessage>(this, (recipient, message) =>
                {
                    RunOnUiThread(RefreshCallUi);
                });
                _messengerRegistered = true;
            }
            catch (Exception)
            {
            }
        }

        private void UnregisterCallChangedListener()
        {
            if (!_messengerRegistered)
                return;
            try
            {
                Ioc.Instance.GetService<IMessengerService>()?.Unregister<PhoneCallChangedMessage>(this);
            }
            catch (Exception)
            {
            }
            _messengerRegistered = false;
        }

        private TextView CreateChip(string text, string backgroundHex, string textHex)
        {
            var button = new TextView(this)
            {
                Text = text,
                TextSize = 13.5f,
                Gravity = GravityFlags.Center,
            };
            button.SetTextColor(Color.ParseColor(textHex));
            int hPad = (int)(12 * _density);
            int vPad = (int)(10 * _density);
            button.SetPadding(hPad, vPad, hPad, vPad);
            var bg = new GradientDrawable();
            bg.SetColor(Color.ParseColor(backgroundHex));
            bg.SetCornerRadius(12 * _density);
            bg.SetStroke((int)(1 * _density), Color.ParseColor("#333333"));
            button.Background = bg;
            button.Clickable = true;
            button.Focusable = true;
            return button;
        }

        private LinearLayout.LayoutParams CreateWeightedParams()
        {
            var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            int gap = (int)(4 * _density);
            lp.SetMargins(gap, 0, gap, 0);
            return lp;
        }

        private void ApplyToggleStyle(TextView button, bool active)
        {
            if (button == null)
                return;
            var bg = new GradientDrawable();
            bg.SetColor(Color.ParseColor(active ? "#1B5E20" : "#1A1A1A"));
            bg.SetCornerRadius(12 * _density);
            bg.SetStroke((int)(1 * _density), Color.ParseColor(active ? "#2E7D32" : "#333333"));
            button.Background = bg;
            button.SetTextColor(Color.ParseColor(active ? "#FFFFFF" : "#CCCCCC"));
        }

        private string GetLabel(string key, string fallback)
        {
            try
            {
                string value = Ioc.Instance.GetService<ILanguageService>()?[key];
                if (!string.IsNullOrWhiteSpace(value) && value != key)
                    return value;
            }
            catch (Exception)
            {
            }
            return fallback;
        }
    }
}
