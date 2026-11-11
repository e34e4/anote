// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using Uri = Android.Net.Uri;
using Button = Android.Widget.Button;

namespace SilentNotes
{
    /// <summary>
    /// Simple video cut and convert UI. Requires ffmpeg at <see cref="AnoteFfmpeg.FfmpegDir"/> for full quality.
    /// </summary>
    [Activity(
        Label = "aNote Video Editor",
        Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
        Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class VideoEditorActivity : Activity
    {
        public const string ExtraPath = "silentnotes.videoeditor.path";
        public const string ExtraFloating = "silentnotes.videoeditor.floating";

        private string _sourcePath;
        private TextView _statusView;
        private EditText _cutStartEdit;
        private EditText _cutEndEdit;
        private EditText _formatEdit;
        private EditText _widthEdit;
        private EditText _heightEdit;
        private EditText _bitrateEdit;
        private EditText _sizePercentEdit;
        private EditText _maxSizeMbEdit;
        private Button _cutButton;
        private Button _convertButton;
        private bool _busy;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            if (Intent?.GetBooleanExtra(ExtraFloating, false) == true)
                SetTheme(global::Android.Resource.Style.ThemeDeviceDefaultDialogNoActionBar);

            base.OnCreate(savedInstanceState);

            if (Intent?.GetBooleanExtra(ExtraFloating, false) == true)
                ConfigureFloatingWindow();

            _sourcePath = Intent?.GetStringExtra(ExtraPath);
            if (string.IsNullOrWhiteSpace(_sourcePath) || !File.Exists(_sourcePath))
            {
                Toast.MakeText(this, "Video file not found", ToastLength.Short).Show();
                Finish();
                return;
            }

            BuildUi();
            RefreshFfmpegStatus();
        }

        private void ConfigureFloatingWindow()
        {
            if (Window == null)
                return;

            SetFinishOnTouchOutside(true);
            Window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
            Window.AddFlags(WindowManagerFlags.DimBehind);

            WindowManagerLayoutParams lp = Window.Attributes;
            if (lp != null)
            {
                DisplayMetrics metrics = Resources.DisplayMetrics;
                lp.DimAmount = 0.55f;
                lp.Width = (int)(metrics.WidthPixels * 0.96f);
                lp.Height = (int)(metrics.HeightPixels * 0.88f);
                lp.Gravity = GravityFlags.Center;
                Window.Attributes = lp;
            }
        }

        private void BuildUi()
        {
            int pad = (int)(12 * Resources.DisplayMetrics.Density);
            var scroll = new ScrollView(this);
            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetPadding(pad, pad, pad, pad);
            root.SetBackgroundColor(Color.ParseColor("#111111"));

            var title = new TextView(this)
            {
                Text = Path.GetFileName(_sourcePath) ?? "Video",
                TextSize = 15,
            };
            title.SetTextColor(Color.White);
            title.SetTypeface(title.Typeface, TypefaceStyle.Bold);
            root.AddView(title);

            _statusView = new TextView(this) { TextSize = 12 };
            _statusView.SetTextColor(Color.ParseColor("#B0BEC5"));
            _statusView.SetPadding(0, pad / 2, 0, pad);
            root.AddView(_statusView);

            root.AddView(SectionLabel("Cut segment (stream copy when ffmpeg is present)"));
            _cutStartEdit = LabeledField(root, "Start (sec or mm:ss)", "0");
            _cutEndEdit = LabeledField(root, "End (sec or mm:ss)", "");

            _cutButton = ActionButton("Cut and save");
            _cutButton.Click += async (_, __) => await CutAsync();
            root.AddView(_cutButton);

            root.AddView(SectionLabel("Convert / resize"));
            _formatEdit = LabeledField(root, "Format (mp4, avi, gif, …)", Path.GetExtension(_sourcePath)?.TrimStart('.') ?? "mp4");
            _widthEdit = LabeledField(root, "Width (0 = keep)", "0");
            _heightEdit = LabeledField(root, "Height (0 = keep)", "0");
            _bitrateEdit = LabeledField(root, "Video bitrate (e.g. 2M, 800k)", "");
            _sizePercentEdit = LabeledField(root, "Reduce size by % (optional)", "");
            _maxSizeMbEdit = LabeledField(root, "Target max size MB (optional)", "");

            _convertButton = ActionButton("Convert");
            _convertButton.Click += async (_, __) => await ConvertAsync();
            root.AddView(_convertButton);

            var close = ActionButton("Close");
            close.SetBackgroundColor(Color.ParseColor("#37474F"));
            close.Click += (_, __) => Finish();
            root.AddView(close);

            scroll.AddView(root);
            SetContentView(scroll);
        }

        private TextView SectionLabel(string text)
        {
            var tv = new TextView(this) { Text = text, TextSize = 13 };
            tv.SetTextColor(Color.ParseColor("#4FC3F7"));
            tv.SetPadding(0, (int)(14 * Resources.DisplayMetrics.Density), 0, (int)(4 * Resources.DisplayMetrics.Density));
            return tv;
        }

        private EditText LabeledField(LinearLayout parent, string label, string preset)
        {
            var cap = new TextView(this) { Text = label, TextSize = 11 };
            cap.SetTextColor(Color.LightGray);
            parent.AddView(cap);

            var edit = new EditText(this) { Text = preset };
            edit.SetSingleLine(true);
            edit.SetTextColor(Color.White);
            edit.SetHintTextColor(Color.Gray);
            edit.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
            edit.SetPadding((int)(8 * Resources.DisplayMetrics.Density), (int)(6 * Resources.DisplayMetrics.Density),
                (int)(8 * Resources.DisplayMetrics.Density), (int)(6 * Resources.DisplayMetrics.Density));
            parent.AddView(edit, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent));
            return edit;
        }

        private Button ActionButton(string text)
        {
            var btn = new Button(this) { Text = text, TextSize = 13 };
            btn.SetTextColor(Color.White);
            btn.SetBackgroundColor(Color.ParseColor("#1565C0"));
            var lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = (int)(10 * Resources.DisplayMetrics.Density);
            btn.LayoutParameters = lp;
            return btn;
        }

        private void RefreshFfmpegStatus()
        {
            string bin = AnoteFfmpeg.FindBinary();
            _statusView.Text = bin != null
                ? "ffmpeg: " + Path.GetFileName(bin)
                : "ffmpeg not found in " + AnoteFfmpeg.FfmpegDir + " — install ffmpeg to cut/convert with quality.";
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _cutButton.Enabled = !busy;
            _convertButton.Enabled = !busy;
        }

        private async Task CutAsync()
        {
            if (_busy)
                return;

            if (!AnoteFfmpeg.TryParseDuration(_cutStartEdit.Text, out double startSec))
            {
                Toast.MakeText(this, "Invalid start time", ToastLength.Short).Show();
                return;
            }

            if (!AnoteFfmpeg.TryParseDuration(_cutEndEdit.Text, out double endSec) || endSec <= startSec)
            {
                Toast.MakeText(this, "Invalid end time (must be after start)", ToastLength.Short).Show();
                return;
            }

            string ffmpeg = AnoteFfmpeg.FindBinary();
            if (ffmpeg == null)
            {
                Toast.MakeText(this, "Cut requires ffmpeg in " + AnoteFfmpeg.FfmpegDir, ToastLength.Long).Show();
                return;
            }

            string ext = Path.GetExtension(_sourcePath);
            if (string.IsNullOrEmpty(ext))
                ext = ".mp4";
            string outPath = NextOutputPath(Path.GetDirectoryName(_sourcePath), Path.GetFileNameWithoutExtension(_sourcePath) + "_cut", ext);

            SetBusy(true);
            _statusView.Text = "Cutting…";

            string start = startSec.ToString(CultureInfo.InvariantCulture);
            string end = endSec.ToString(CultureInfo.InvariantCulture);
            var args = new List<string>
            {
                "-y",
                "-ss", start,
                "-to", end,
                "-i", _sourcePath,
                "-c", "copy",
                outPath,
            };

            (int code, string output) = await AnoteFfmpeg.RunAsync(args).ConfigureAwait(true);
            SetBusy(false);

            if (code == 0 && File.Exists(outPath))
            {
                _statusView.Text = "Saved: " + outPath;
                Toast.MakeText(this, "Cut saved", ToastLength.Short).Show();
            }
            else
            {
                _statusView.Text = string.IsNullOrWhiteSpace(output) ? "Cut failed" : output;
                Toast.MakeText(this, "Cut failed", ToastLength.Long).Show();
            }
        }

        private async Task ConvertAsync()
        {
            if (_busy)
                return;

            string ffmpeg = AnoteFfmpeg.FindBinary();
            if (ffmpeg == null)
            {
                Toast.MakeText(this, "Convert requires ffmpeg in " + AnoteFfmpeg.FfmpegDir, ToastLength.Long).Show();
                return;
            }

            string fmt = (_formatEdit.Text ?? "mp4").Trim().TrimStart('.');
            if (string.IsNullOrEmpty(fmt))
                fmt = "mp4";

            int.TryParse(_widthEdit.Text, out int width);
            int.TryParse(_heightEdit.Text, out int height);

            string bitrate = (_bitrateEdit.Text ?? string.Empty).Trim();
            string percentText = (_sizePercentEdit.Text ?? string.Empty).Trim();
            string maxMbText = (_maxSizeMbEdit.Text ?? string.Empty).Trim();

            string dir = Path.GetDirectoryName(_sourcePath);
            string outPath = NextOutputPath(dir, Path.GetFileNameWithoutExtension(_sourcePath) + "_converted", "." + fmt);

            var args = new List<string> { "-y", "-i", _sourcePath };

            if (width > 0 || height > 0)
            {
                string w = width > 0 ? width.ToString(CultureInfo.InvariantCulture) : "-2";
                string h = height > 0 ? height.ToString(CultureInfo.InvariantCulture) : "-2";
                args.Add("-vf");
                args.Add($"scale={w}:{h}");
            }

            if (!string.IsNullOrEmpty(bitrate))
            {
                args.Add("-b:v");
                args.Add(bitrate);
            }

            if (double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out double reducePct) &&
                reducePct > 0 && reducePct < 100)
            {
                // Approximate smaller bitrate when user asks to shrink by percentage.
                if (string.IsNullOrEmpty(bitrate))
                {
                    args.Add("-crf");
                    int crf = (int)Math.Clamp(28 + reducePct / 5, 23, 40);
                    args.Add(crf.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (double.TryParse(maxMbText, NumberStyles.Float, CultureInfo.InvariantCulture, out double maxMb) && maxMb > 0)
            {
                args.Add("-fs");
                long bytes = (long)(maxMb * 1024 * 1024);
                args.Add(bytes.ToString(CultureInfo.InvariantCulture));
            }

            if (string.Equals(fmt, "gif", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("-an");
            }

            args.Add(outPath);

            SetBusy(true);
            _statusView.Text = "Converting…";
            (int code, string output) = await AnoteFfmpeg.RunAsync(args).ConfigureAwait(true);
            SetBusy(false);

            if (code == 0 && File.Exists(outPath))
            {
                _statusView.Text = "Saved: " + outPath;
                Toast.MakeText(this, "Convert saved", ToastLength.Short).Show();
            }
            else
            {
                _statusView.Text = string.IsNullOrWhiteSpace(output) ? "Convert failed" : output;
                Toast.MakeText(this, "Convert failed", ToastLength.Long).Show();
            }
        }

        private static string NextOutputPath(string dir, string baseName, string extension)
        {
            if (string.IsNullOrEmpty(dir))
                dir = SilentNotes.Workers.AnotePaths.Down;
            if (!extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;

            for (int i = 0; i < 100; i++)
            {
                string suffix = i == 0 ? string.Empty : "_" + i;
                string path = Path.Combine(dir, baseName + suffix + extension);
                if (!File.Exists(path))
                    return path;
            }

            return Path.Combine(dir, baseName + "_" + DateTime.Now.Ticks + extension);
        }

        public static Intent CreateIntent(Context context, string path, bool floating = false)
        {
            var intent = new Intent(context, typeof(VideoEditorActivity));
            intent.PutExtra(ExtraPath, path);
            intent.PutExtra(ExtraFloating, floating);
            return intent;
        }
    }
}
