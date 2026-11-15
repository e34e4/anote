// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using SilentNotes.Models;
using SilentNotes.Platforms;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Small dialog which captures a scratchpad entry without opening the main aNote UI.
    /// </summary>
    [Activity(
        Label = "aNote scratchpad",
        Theme = "@android:style/Theme.Translucent.NoTitleBar",
        Exported = false,
        NoHistory = true,
        LaunchMode = LaunchMode.SingleTop,
        ExcludeFromRecents = true)]
    public class ScratchCaptureActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var edit = new EditText(this)
            {
                Hint = "Scratchpad note",
                Gravity = GravityFlags.Top | GravityFlags.Start,
                InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine | InputTypes.TextFlagCapSentences,
            };
            edit.SetMinLines(4);
            int padding = (int)(16 * Resources.DisplayMetrics.Density);
            var container = new FrameLayout(this);
            container.SetPadding(padding, padding, padding, padding);
            container.AddView(edit, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, (int)(180 * Resources.DisplayMetrics.Density)));

            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("aNote scratchpad");
            builder.SetView(container);
            builder.SetPositiveButton("Save", (sender, args) => Save(edit.Text));
            builder.SetNegativeButton("Cancel", (sender, args) => { });
            builder.SetOnDismissListener(new DismissListener(Finish));
            builder.Show();
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

        private sealed class DismissListener : Java.Lang.Object, IDialogInterfaceOnDismissListener
        {
            private readonly Action _dismiss;

            public DismissListener(Action dismiss)
            {
                _dismiss = dismiss;
            }

            public void OnDismiss(IDialogInterface dialog)
            {
                _dismiss?.Invoke();
            }
        }
    }
}
