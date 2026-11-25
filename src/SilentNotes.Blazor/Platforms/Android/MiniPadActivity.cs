// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using AButton = Android.Widget.Button;
using AScrollView = Android.Widget.ScrollView;
using SilentNotes.Models;
using SilentNotes.Platforms;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Floating dialog for scratchpad text and quick paste from clipboard history, without opening
    /// the full aNote shell.
    /// </summary>
    [Activity(
        Label = "aNote mini-pad",
        Theme = "@android:style/Theme.DeviceDefault.Dialog.NoActionBar",
        Exported = false,
        NoHistory = true,
        LaunchMode = LaunchMode.SingleTop,
        ExcludeFromRecents = true,
        WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateVisible)]
    public class MiniPadActivity : Activity
    {
        internal const string ExtraDraft = "aNoteMiniPadDraft";
        private const string PreferencesName = "anote_floating_button";
        private const string PreferenceKeyDraft = "mini_pad_draft";
        private const int MaxClipboardItems = 8;

        private EditText _edit;
        private LinearLayout _clipList;

        /// <inheritdoc/>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            BuildUi();
        }

        /// <inheritdoc/>
        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            if (_edit == null)
                return;

            string draft = intent?.GetStringExtra(ExtraDraft);
            if (!string.IsNullOrEmpty(draft))
                _edit.Text = draft;
            else
                _edit.Text = LoadDraft();
            RefreshClipList();
        }

        private void BuildUi()
        {
            float density = Resources.DisplayMetrics.Density;
            int padding = (int)(12 * density);

            var root = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            root.SetPadding(padding, padding, padding, padding);
            root.SetBackgroundColor(global::Android.Graphics.Color.Argb(245, 22, 22, 22));

            var title = new TextView(this)
            {
                Text = "Mini-pad",
                TextSize = 18f,
            };
            title.SetTypeface(global::Android.Graphics.Typeface.DefaultBold, global::Android.Graphics.TypefaceStyle.Bold);
            title.SetTextColor(global::Android.Graphics.Color.White);
            root.AddView(title);

            _edit = new EditText(this)
            {
                Gravity = GravityFlags.Top | GravityFlags.Start,
                InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine | InputTypes.TextFlagCapSentences,
            };
            _edit.Hint = "Scratchpad text…";
            _edit.SetMinLines(4);
            _edit.SetTextColor(global::Android.Graphics.Color.White);
            _edit.SetHintTextColor(global::Android.Graphics.Color.Gray);
            string initial = Intent?.GetStringExtra(ExtraDraft);
            _edit.Text = !string.IsNullOrEmpty(initial) ? initial : LoadDraft();

            root.AddView(_edit, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, (int)(120 * density))
            {
                TopMargin = (int)(8 * density),
            });

            var clipLabel = new TextView(this)
            {
                Text = "Recent clipboard",
                TextSize = 12f,
            };
            clipLabel.SetTextColor(global::Android.Graphics.Color.ParseColor("#94A3B8"));
            clipLabel.SetPadding(0, (int)(8 * density), 0, (int)(4 * density));
            root.AddView(clipLabel);

            var clipScroll = new AScrollView(this);
            _clipList = new LinearLayout(this) { Orientation = Orientation.Vertical };
            clipScroll.AddView(_clipList);
            root.AddView(clipScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, (int)(100 * density))
            {
                BottomMargin = (int)(8 * density),
            });
            RefreshClipList();

            var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            buttons.SetGravity(GravityFlags.End);

            var collapseBtn = CreateButton("Collapse", density, () => FinishAfterDraft());
            var clearBtn = CreateButton("Clear", density, ClearText);
            var saveBtn = CreateButton("Save", density, () => Save(_edit.Text));
            buttons.AddView(collapseBtn);
            buttons.AddView(clearBtn);
            buttons.AddView(saveBtn);
            root.AddView(buttons);

            SetContentView(root);
        }

        private AButton CreateButton(string label, float density, System.Action onClick)
        {
            var button = new AButton(this) { Text = label };
            button.SetAllCaps(false);
            int hPad = (int)(8 * density);
            button.SetPadding(hPad, 0, hPad, 0);
            button.Click += (s, e) => onClick?.Invoke();
            button.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                LeftMargin = (int)(6 * density),
            };
            return button;
        }

        private void RefreshClipList()
        {
            if (_clipList == null)
                return;

            _clipList.RemoveAllViews();
            float density = Resources.DisplayMetrics.Density;

            foreach (string clip in GetRecentClipboardTexts())
            {
                string text = clip;
                var row = new TextView(this)
                {
                    Text = TruncatePreview(text, 120),
                    TextSize = 12f,
                };
                row.SetTextColor(global::Android.Graphics.Color.ParseColor("#E2E8F0"));
                row.SetPadding(0, (int)(4 * density), 0, (int)(4 * density));
                row.Clickable = true;
                row.Click += (s, e) => InsertClip(text);
                _clipList.AddView(row);
            }

            if (_clipList.ChildCount == 0)
            {
                var empty = new TextView(this)
                {
                    Text = "No recent text clips",
                    TextSize = 11f,
                };
                empty.SetTextColor(global::Android.Graphics.Color.Gray);
                _clipList.AddView(empty);
            }
        }

        private void InsertClip(string text)
        {
            if (string.IsNullOrEmpty(text) || _edit == null)
                return;

            if (_edit.Text?.Length > 0)
                _edit.Append("\n");
            _edit.Append(text);
        }

        private void ClearText()
        {
            if (_edit != null)
                _edit.Text = string.Empty;
        }

        private void FinishAfterDraft()
        {
            StoreDraft(_edit?.Text);
            Finish();
        }

        /// <inheritdoc/>
        public override void OnBackPressed()
        {
            FinishAfterDraft();
        }

        /// <inheritdoc/>
        protected override void OnPause()
        {
            StoreDraft(_edit?.Text);
            FloatingButtonAccessibilityService.Instance?.RefreshMiniPadOverlayPreview();
            base.OnPause();
        }

        private void Save(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                IRepositoryStorageService repositoryService = Ioc.Instance.GetService<IRepositoryStorageService>();
                repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                CaptureNoteHelper.AppendScratchpad(repository, text);
                repositoryService.TrySaveRepository(repository);
                Ioc.Instance.GetService<IMessengerService>()?.Send(new NotesChangedMessage());
                AnoteToast.Show(this, "Saved to the Scratchpad note", FeedbackSeverity.Info);
                StoreDraft(string.Empty);
                if (_edit != null)
                    _edit.Text = string.Empty;
            }
            catch (Exception)
            {
                try
                {
                    int limit = Ioc.Instance.GetService<ISettingsService>()?.LoadSettingsOrDefault()?.ClipboardHistorySize ?? 500;
                    Ioc.Instance.GetService<IClipboardHistoryService>()?.AddText(text.Trim(), limit);
                    AnoteToast.Show(this, "Saved to the clipboard history", FeedbackSeverity.Warning);
                }
                catch (Exception)
                {
                }
            }
        }

        internal static string LoadDraft(Context context)
        {
            if (context == null)
                return string.Empty;
            try
            {
                return context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
                    ?.GetString(PreferenceKeyDraft, string.Empty) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private string LoadDraft() => LoadDraft(this);

        internal static void StoreDraft(Context context, string text)
        {
            if (context == null)
                return;
            try
            {
                context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
                    ?.Edit()
                    ?.PutString(PreferenceKeyDraft, text ?? string.Empty)
                    ?.Apply();
            }
            catch (Exception)
            {
            }
        }

        private static void StoreDraft(string text)
        {
            Context context = Android.App.Application.Context;
            if (context != null)
                StoreDraft(context, text);
        }

        internal static IList<string> GetRecentClipboardTexts(int max = MaxClipboardItems)
        {
            try
            {
                ClipboardHistoryModel model = Ioc.Instance.GetService<IClipboardHistoryService>()?.LoadHistoryOrDefault();
                if (model?.Items == null)
                    return Array.Empty<string>();

                return model.Items
                    .Where(item => !item.IsDeleted && item.Kind == ClipboardItemKind.Text && !string.IsNullOrWhiteSpace(item.Text))
                    .Take(max)
                    .Select(item => item.Text)
                    .ToList();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        internal static string TruncatePreview(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            string oneLine = text.Replace("\r", " ").Replace("\n", " ");
            return oneLine.Length <= maxLen ? oneLine : oneLine.Substring(0, maxLen - 1) + "…";
        }
    }
}