// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using SilentNotes.Services;
using SilentNotes.Workers;
using Orientation = Android.Widget.Orientation;
using ProgressBar = Android.Widget.ProgressBar;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Native Android dialogs for Player (play vs yt-dlp) and URLCheck (quality + download).
    /// </summary>
    internal sealed class YtDlpDownloadPromptService : IYtDlpDownloadPromptService
    {
        private readonly IAppContextService _appContext;
        private readonly IYtDlpDownloadService _downloadService;

        public YtDlpDownloadPromptService(
            IAppContextService appContext,
            IYtDlpDownloadService downloadService)
        {
            _appContext = appContext ?? throw new ArgumentNullException(nameof(appContext));
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        }

        /// <inheritdoc/>
        public async Task HandlePlayOrDownloadAsync(string url, Func<Task> playAsync)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            Activity activity = _appContext.RootActivity;
            if (activity == null || activity.IsFinishing)
            {
                if (playAsync != null)
                    await playAsync().ConfigureAwait(true);
                return;
            }

            AlertDialogHelper.DialogResult action = await AlertDialogHelper.ShowAsync(
                activity,
                url.Trim(),
                "Open URL",
                "Play",
                "yt-dlp",
                "Cancel").ConfigureAwait(true);

            if (action == AlertDialogHelper.DialogResult.Positive)
            {
                if (playAsync != null)
                    await playAsync().ConfigureAwait(true);
                return;
            }

            if (action == AlertDialogHelper.DialogResult.Neutral)
                await YtDlpDownloadUi.PromptQualityAndDownloadAsync(activity, _downloadService, url.Trim())
                    .ConfigureAwait(true);
        }

        /// <inheritdoc/>
        public Task PromptQualityAndDownloadAsync(string url)
        {
            Activity activity = _appContext.RootActivity;
            Context context = (Context)activity ?? _appContext.Context;
            return YtDlpDownloadUi.PromptQualityAndDownloadAsync(context, _downloadService, url);
        }
    }

    /// <summary>
    /// Static Android UI helpers for yt-dlp quality selection and progress — reusable from
    /// <see cref="YtDlpDownloadPromptService"/> and <see cref="UrlCheckActivity"/>.
    /// </summary>
    internal static class YtDlpDownloadUi
    {
        public static async Task PromptQualityAndDownloadAsync(
            Context context,
            IYtDlpDownloadService downloadService,
            string url)
        {
            if (context == null || string.IsNullOrWhiteSpace(url) || downloadService == null)
                return;

            url = url.Trim();
            YtDlpQualityChoice choice = await PickQualityAsync(context).ConfigureAwait(true);
            if (choice == null)
                return;

            if (!downloadService.IsAvailable)
            {
                Toast.MakeText(context, "yt-dlp not ready. Open Settings → Update yt-dlp.", ToastLength.Long)?.Show();
                AnoteFileLog.Append("ytdlp", "Not available when downloading " + url);
                return;
            }

            await RunDownloadWithProgressAsync(context, downloadService, url, choice).ConfigureAwait(true);
        }

        private static Task<YtDlpQualityChoice> PickQualityAsync(Context context)
        {
            var tcs = new TaskCompletionSource<YtDlpQualityChoice>();
            var labels = new string[YtDlpQualityChoice.DefaultChoices.Count];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = YtDlpQualityChoice.DefaultChoices[i].Label;

            int selected = 0;
            void Show()
            {
                try
                {
                    var builder = new AlertDialog.Builder(context);
                    builder.SetTitle("yt-dlp quality");
                    builder.SetSingleChoiceItems(labels, selected, (s, e) =>
                    {
                        selected = e.Which;
                    });
                    builder.SetPositiveButton("Download", (s, e) =>
                    {
                        if (selected >= 0 && selected < YtDlpQualityChoice.DefaultChoices.Count)
                            tcs.TrySetResult(YtDlpQualityChoice.DefaultChoices[selected]);
                        else
                            tcs.TrySetResult(YtDlpQualityChoice.DefaultChoices[0]);
                    });
                    builder.SetNegativeButton("Cancel", (s, e) => tcs.TrySetResult(null));
                    builder.SetOnCancelListener(new CancelListener(() => tcs.TrySetResult(null)));
                    builder.Show();
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Append("ytdlp", "Quality dialog failed: " + ex);
                    tcs.TrySetResult(null);
                }
            }

            if (context is Activity activity)
                activity.RunOnUiThread(Show);
            else
                new Handler(Looper.MainLooper).Post(Show);

            return tcs.Task;
        }

        private static async Task RunDownloadWithProgressAsync(
            Context context,
            IYtDlpDownloadService downloadService,
            string url,
            YtDlpQualityChoice choice)
        {
            ProgressDialogState state = null;
            try
            {
                state = await ShowProgressDialogAsync(context, "Downloading…").ConfigureAwait(true);
                var progress = new Progress<YtDlpDownloadProgress>(p =>
                {
                    if (state == null || state.Dialog == null)
                        return;
                    void Update()
                    {
                        try
                        {
                            if (p.Percent is double pct)
                            {
                                state.Bar.Indeterminate = false;
                                state.Bar.Progress = (int)Math.Clamp(Math.Round(pct), 0, 100);
                            }

                            if (!string.IsNullOrEmpty(p.StatusText))
                                state.Status.Text = p.StatusText;
                        }
                        catch (Exception)
                        {
                        }
                    }

                    if (context is Activity act)
                        act.RunOnUiThread(Update);
                    else
                        new Handler(Looper.MainLooper).Post(Update);
                });

                YtDlpDownloadResult result = await downloadService.DownloadAsync(url, choice, progress)
                    .ConfigureAwait(true);

                DismissProgress(state);

                string message = result.Success
                    ? (result.Message ?? "Download complete")
                    : (result.Message ?? "Download failed");
                Toast.MakeText(context, message, result.Success ? ToastLength.Long : ToastLength.Long)?.Show();
            }
            catch (Exception ex)
            {
                AnoteFileLog.Append("ytdlp", "Progress UI download failed: " + ex);
                DismissProgress(state);
                Toast.MakeText(context, ex.Message, ToastLength.Long)?.Show();
            }
        }

        private static Task<ProgressDialogState> ShowProgressDialogAsync(Context context, string title)
        {
            var tcs = new TaskCompletionSource<ProgressDialogState>();
            void Show()
            {
                try
                {
                    float density = context.Resources?.DisplayMetrics?.Density ?? 1f;
                    int pad = (int)(16 * density);

                    var layout = new LinearLayout(context)
                    {
                        Orientation = Orientation.Vertical,
                    };
                    layout.SetPadding(pad, pad, pad, pad);

                    var status = new TextView(context)
                    {
                        Text = "Starting…",
                        TextSize = 13f,
                    };
                    layout.AddView(status, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

                    var bar = new ProgressBar(context, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
                    {
                        Indeterminate = true,
                        Max = 100,
                    };
                    var barLp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                    barLp.TopMargin = (int)(12 * density);
                    layout.AddView(bar, barLp);

                    var pathHint = new TextView(context)
                    {
                        Text = "Save to " + AnotePaths.Down,
                        TextSize = 11.5f,
                    };
                    var hintLp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                    hintLp.TopMargin = (int)(8 * density);
                    layout.AddView(pathHint, hintLp);

                    var builder = new AlertDialog.Builder(context);
                    builder.SetTitle(title);
                    builder.SetView(layout);
                    builder.SetCancelable(false);
                    AlertDialog dialog = builder.Create();
                    dialog.Show();
                    tcs.TrySetResult(new ProgressDialogState
                    {
                        Dialog = dialog,
                        Bar = bar,
                        Status = status,
                    });
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Append("ytdlp", "Progress dialog failed: " + ex);
                    tcs.TrySetResult(null);
                }
            }

            if (context is Activity activity)
                activity.RunOnUiThread(Show);
            else
                new Handler(Looper.MainLooper).Post(Show);

            return tcs.Task;
        }

        private static void DismissProgress(ProgressDialogState state)
        {
            if (state?.Dialog == null)
                return;
            try
            {
                state.Dialog.Dismiss();
            }
            catch (Exception)
            {
            }
        }

        private sealed class ProgressDialogState
        {
            public AlertDialog Dialog { get; set; }
            public ProgressBar Bar { get; set; }
            public TextView Status { get; set; }
        }

        private sealed class CancelListener : Java.Lang.Object, IDialogInterfaceOnCancelListener
        {
            private readonly Action _onCancel;

            public CancelListener(Action onCancel)
            {
                _onCancel = onCancel;
            }

            public void OnCancel(IDialogInterface dialog)
            {
                _onCancel?.Invoke();
            }
        }
    }
}
