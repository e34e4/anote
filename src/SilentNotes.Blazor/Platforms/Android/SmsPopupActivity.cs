// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using AScrollView = Android.Widget.ScrollView;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;
using SilentNotes.Models;
using SilentNotes.Platforms;
using SilentNotes.Services;

namespace SilentNotes
{
    /// <summary>
    /// Entry point for tapping an incoming SMS notification. When the floating accessibility overlay
    /// service is active, it immediately delegates to a movable/resizable system floating window and
    /// closes itself without disturbing the foreground app. Otherwise it falls back to showing a
    /// compact floating dialog card directly.
    /// </summary>
    [Activity(
        Label = "aNote SMS",
        Theme = "@android:style/Theme.Translucent.NoTitleBar",
        Exported = false,
        NoHistory = true,
        LaunchMode = LaunchMode.SingleTop,
        TaskAffinity = "",
        ExcludeFromRecents = true)]
    public class SmsPopupActivity : Activity
    {
        public const string ExtraThreadId = "aNoteSmsPopupThreadId";
        public const string ExtraAddress = "aNoteSmsPopupAddress";
        public const string ExtraBody = "aNoteSmsPopupBody";
        public const string ExtraTimestampMs = "aNoteSmsPopupTimestampMs";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            HandleIntent(Intent);
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            Intent = intent;
            HandleIntent(intent);
        }

        private void HandleIntent(Intent intent)
        {
            long threadId = intent?.GetLongExtra(ExtraThreadId, 0) ?? 0;
            string address = intent?.GetStringExtra(ExtraAddress) ?? string.Empty;
            string body = intent?.GetStringExtra(ExtraBody) ?? string.Empty;
            long timestampMs = intent?.GetLongExtra(ExtraTimestampMs, 0) ?? 0;

            if (FloatingButtonAccessibilityService.Instance?.ShowSmsOverlay(threadId, address, body, timestampMs) == true)
            {
                Finish();
#pragma warning disable CS0618
                OverridePendingTransition(0, 0);
#pragma warning restore CS0618
                return;
            }

            BuildFallbackFloatingCard(threadId, address, body, timestampMs);
        }

        private void BuildFallbackFloatingCard(long threadId, string address, string body, long timestampMs)
        {
            float density = Resources.DisplayMetrics.Density;
            int screenW = Resources.DisplayMetrics.WidthPixels;
            int screenH = Resources.DisplayMetrics.HeightPixels;

            var backdrop = new FrameLayout(this);
            backdrop.SetBackgroundColor(Color.Argb(110, 0, 0, 0));
            backdrop.Click += (s, e) => Finish();

            var card = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                Clickable = true,
            };
            int cardPad = (int)(12 * density);
            card.SetPadding(cardPad, cardPad, cardPad, cardPad);

            var cardBg = new GradientDrawable();
            cardBg.SetColor(Color.ParseColor("#F2101010"));
            cardBg.SetCornerRadius(16 * density);
            cardBg.SetStroke((int)(1.5f * density), Color.ParseColor("#38BDF8"));
            card.Background = cardBg;

            string displayName = ResolveSenderDisplayName(address);
            string headerTitle = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : (!string.IsNullOrWhiteSpace(address) ? address : "SMS");

            var headerRow = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };
            headerRow.SetGravity(GravityFlags.CenterVertical);
            headerRow.SetPadding((int)(2 * density), 0, (int)(2 * density), (int)(6 * density));

            var titleView = new TextView(this)
            {
                Text = "💬 " + headerTitle,
                TextSize = 14.5f,
                Ellipsize = global::Android.Text.TextUtils.TruncateAt.End,
            };
            titleView.SetSingleLine(true);
            titleView.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            titleView.SetTextColor(Color.White);
            headerRow.AddView(titleView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            int btnPad = (int)(6 * density);
            var copyBtn = new TextView(this)
            {
                Text = "Copy",
                TextSize = 12f,
                Gravity = GravityFlags.Center,
            };
            copyBtn.SetTextColor(Color.ParseColor("#94A3B8"));
            copyBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
            copyBtn.Click += (s, e) => CopyToClipboard(this, body);
            headerRow.AddView(copyBtn);

            var closeBtn = new TextView(this)
            {
                Text = "✕",
                TextSize = 14f,
                Gravity = GravityFlags.Center,
            };
            closeBtn.SetTextColor(Color.ParseColor("#BDBDBD"));
            closeBtn.SetPadding(btnPad, (int)(2 * density), btnPad, (int)(2 * density));
            closeBtn.Click += (s, e) => Finish();
            headerRow.AddView(closeBtn);

            card.AddView(headerRow);

            string subtitle = BuildSubtitle(displayName, address, timestampMs);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleView = new TextView(this)
                {
                    Text = subtitle,
                    TextSize = 11.5f,
                };
                subtitleView.SetTextColor(Color.ParseColor("#94A3B8"));
                subtitleView.SetPadding((int)(2 * density), 0, (int)(2 * density), (int)(6 * density));
                card.AddView(subtitleView);
            }

            var scrollView = new AScrollView(this);
            var bodyView = new TextView(this)
            {
                Text = body ?? string.Empty,
                TextSize = 14f,
            };
            bodyView.SetTextColor(Color.ParseColor("#F1F5F9"));
            bodyView.SetLineSpacing(0f, 1.2f);
            bodyView.SetTextIsSelectable(true);
            bodyView.SetPadding((int)(2 * density), (int)(2 * density), (int)(2 * density), (int)(6 * density));
            scrollView.AddView(bodyView);
            card.AddView(scrollView, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));

            var openBtn = new TextView(this)
            {
                Text = "Open in aNote",
                TextSize = 13f,
                Gravity = GravityFlags.Center,
            };
            openBtn.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            openBtn.SetTextColor(Color.ParseColor("#0B1120"));
            openBtn.SetPadding((int)(12 * density), (int)(8 * density), (int)(12 * density), (int)(8 * density));
            var openBg = new GradientDrawable();
            openBg.SetColor(Color.ParseColor("#38BDF8"));
            openBg.SetCornerRadius(10 * density);
            openBtn.Background = openBg;
            openBtn.Click += (s, e) =>
            {
                OpenConversationInMainApp(this, threadId, address);
                Finish();
            };
            var openParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = (int)(8 * density),
            };
            card.AddView(openBtn, openParams);

            int cardW = (int)Math.Min(screenW * 0.90f, 380 * density);
            int cardH = (int)Math.Min(screenH * 0.45f, 300 * density);
            var cardParams = new FrameLayout.LayoutParams(cardW, cardH)
            {
                Gravity = GravityFlags.Center,
            };
            backdrop.AddView(card, cardParams);
            SetContentView(backdrop);
        }

        /// <summary>
        /// Opens the full aNote app with the SMS tab activated and the given conversation opened.
        /// </summary>
        internal static void OpenConversationInMainApp(Context context, long threadId, string address)
        {
            if (context == null)
                return;

            // Set pending request in-process first (same process as MainActivity), and also pass
            // extras on the Intent so cold starts and OEM relaunches both restore the conversation.
            SmsIntentRequest.Pending = new SmsIntentRequest
            {
                ThreadId = threadId > 0 ? threadId : 0,
                PhoneNumber = string.IsNullOrWhiteSpace(address) ? null : address,
                OpenConversation = true,
            };

            var intent = new Intent(context, typeof(MainActivity));
            intent.PutExtra(FloatingButtonAccessibilityService.ExtraOpenSmsThreadId, threadId);
            if (!string.IsNullOrWhiteSpace(address))
                intent.PutExtra(FloatingButtonAccessibilityService.ExtraOpenSmsAddress, address);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ReorderToFront | ActivityFlags.ClearTop);
            context.StartActivity(intent);
        }

        internal static void CopyToClipboard(Context context, string text)
        {
            if (context == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                var clipboard = (ClipboardManager)context.GetSystemService(Context.ClipboardService);
                if (clipboard != null)
                {
                    clipboard.PrimaryClip = ClipData.NewPlainText("SMS", text);
                    AnoteToast.Show(context, "Copied", FeedbackSeverity.Info);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static string BuildSubtitle(string displayName, string address, long timestampMs)
        {
            string timeText = timestampMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).ToLocalTime().ToString("HH:mm")
                : null;

            bool showAddress = !string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(address);
            if (showAddress && !string.IsNullOrEmpty(timeText))
                return address + " · " + timeText;
            if (showAddress)
                return address;
            return timeText ?? string.Empty;
        }

        internal static string ResolveSenderDisplayName(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            try
            {
                IRepositoryStorageService repositoryService = Ioc.Instance.GetService<IRepositoryStorageService>();
                if (repositoryService == null)
                    return null;

                repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                if (repository?.Contacts == null)
                    return null;

                string target = NormalizeDigits(address);
                if (string.IsNullOrEmpty(target))
                    return null;

                foreach (ContactModel contact in repository.Contacts)
                {
                    if (contact?.Phones == null)
                        continue;
                    foreach (string phone in contact.Phones)
                    {
                        if (string.Equals(NormalizeDigits(phone), target, StringComparison.Ordinal))
                            return contact.Name;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static string NormalizeDigits(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return null;
            string digits = new string(number.Where(char.IsDigit).ToArray());
            if (digits.Length > 9)
                digits = digits.Substring(digits.Length - 9);
            return string.IsNullOrEmpty(digits) ? null : digits;
        }
    }
}
