// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.ExifInterface.Media;
using AButton = Android.Widget.Button;
using Color = Android.Graphics.Color;
using IOPath = System.IO.Path;
using MediaOrientation = Android.Media.Orientation;
using SysUri = System.Uri;
using ViewOrientation = Android.Widget.Orientation;

namespace SilentNotes
{
    /// <summary>
    /// Internal image editor with free crop, drawing tools, undo/redo, convert, and EXIF clear.
    /// </summary>
    [Activity(
        Label = "aNote Image Editor",
        Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
        Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class ImageEditorActivity : Activity
    {
        public const string ExtraPath = "silentnotes.imageeditor.path";
        public const string ExtraUri = "silentnotes.imageeditor.uri";

        private const int MaxHistory = 20;
        private const string AbroFontsDir = "/sdcard/abro/fonts";

        private static readonly Color[] Palette =
        {
            Color.White, Color.Black, Color.Red, Color.Yellow, Color.Green,
            Color.Cyan, Color.Blue, Color.Magenta, Color.Orange, Color.Gray,
        };

        private enum ToolMode
        {
            None,
            Crop,
            Brush,
            Blur,
            Text,
            Emoji,
            Arrow,
        }

        private TextView _headerInfo;
        private TextView _status;
        private EditorCanvasView _canvas;
        private AButton _undoBtn;
        private AButton _redoBtn;
        private AButton _applyBtn;
        private AButton _cancelToolBtn;

        private string _path;
        private Bitmap _bitmap;
        private readonly Stack<Bitmap> _undo = new Stack<Bitmap>();
        private readonly Stack<Bitmap> _redo = new Stack<Bitmap>();

        private ToolMode _mode = ToolMode.None;
        private int _brushSize = 6;
        private int _brushColorIndex;
        private int _arrowSizeStep = 5;
        private int _arrowColorIndex;
        private int _emojiSizeStep = 5;
        private string _pendingText = string.Empty;
        private Typeface _pendingTypeface = Typeface.Default;
        private string _pendingFontLabel = "Default";
        private string _pendingEmoji = "😀";
        private string _outputFormat = ".jpg";
        private int _outputQuality = 92;
        private bool _clearAllExif;
        private string _exifDateTime = string.Empty;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _path = Intent?.GetStringExtra(ExtraPath);
            if (string.IsNullOrWhiteSpace(_path))
            {
                string uriText = Intent?.GetStringExtra(ExtraUri);
                if (!string.IsNullOrWhiteSpace(uriText) && uriText.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    && SysUri.TryCreate(uriText, UriKind.Absolute, out SysUri fileUri))
                {
                    _path = fileUri.LocalPath;
                }
            }

            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
            {
                Toast.MakeText(this, "Image not found", ToastLength.Short).Show();
                Finish();
                return;
            }

            try
            {
                _bitmap = BitmapFactory.DecodeFile(_path)?.Copy(Bitmap.Config.Argb8888, true);
                _outputFormat = NormalizeExt(IOPath.GetExtension(_path));
                LoadExifDate();
            }
            catch
            {
                _bitmap = null;
            }

            if (_bitmap == null)
            {
                Toast.MakeText(this, "Could not open image", ToastLength.Short).Show();
                Finish();
                return;
            }

            BuildUi();
            RefreshHeader();
            RefreshStatus();
            UpdateHistoryButtons();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            RecycleStack(_undo);
            RecycleStack(_redo);
            try { _bitmap?.Recycle(); } catch { }
            _bitmap = null;
        }

        private void BuildUi()
        {
            var root = new LinearLayout(this) { Orientation = ViewOrientation.Vertical };
            root.SetBackgroundColor(Color.Black);
            root.SetPadding(12, 16, 12, 12);

            var header = new LinearLayout(this) { Orientation = ViewOrientation.Horizontal };
            _headerInfo = new TextView(this)
            {
                TextSize = 12,
                Ellipsize = TextUtils.TruncateAt.Middle,
            };
            _headerInfo.SetTextColor(Color.White);
            _headerInfo.SetMaxLines(3);
            _headerInfo.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            var close = new AButton(this) { Text = "✕" };
            close.Click += (_, __) => Finish();
            header.AddView(_headerInfo);
            header.AddView(close);
            root.AddView(header);

            _canvas = new EditorCanvasView(this);
            _canvas.SetBitmap(_bitmap);
            _canvas.SelectionFinished += OnSelectionFinished;
            _canvas.StrokeFinished += OnBrushStrokeFinished;
            _canvas.LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f);
            root.AddView(_canvas);

            _status = new TextView(this) { TextSize = 12 };
            _status.SetTextColor(Color.LightGray);
            root.AddView(_status);

            root.AddView(CreateButtonRow(
                ("Undo", () => Undo()),
                ("Redo", () => Redo()),
                ("Apply", () => ApplyPendingTool()),
                ("Cancel", () => CancelTool())));
            _undoBtn = (AButton)((ViewGroup)root.GetChildAt(root.ChildCount - 1)).GetChildAt(0);
            _redoBtn = (AButton)((ViewGroup)root.GetChildAt(root.ChildCount - 1)).GetChildAt(1);
            _applyBtn = (AButton)((ViewGroup)root.GetChildAt(root.ChildCount - 1)).GetChildAt(2);
            _cancelToolBtn = (AButton)((ViewGroup)root.GetChildAt(root.ChildCount - 1)).GetChildAt(3);

            root.AddView(WrapScroll(CreateButtonRow(
                ("Rotate 90°", () => Rotate90()),
                ("Crop", () => EnterMode(ToolMode.Crop)),
                ("Size −10%", () => ShrinkContent10()),
                ("Size +10%", () => ExpandCanvas10()))));

            root.AddView(WrapScroll(CreateButtonRow(
                ("Brush", () => ShowBrushOptions()),
                ("Blur", () => EnterMode(ToolMode.Blur)),
                ("Text", () => ShowTextOptions()),
                ("Emoji", () => ShowEmojiOptions()),
                ("Arrow", () => ShowArrowOptions()))));

            root.AddView(WrapScroll(CreateButtonRow(
                ("Clear EXIF", () => ClearExifNow()),
                ("Convert", () => ShowConvertDialog()),
                ("Save", () => Save(overwrite: true)),
                ("Save as", () => PromptSaveAs()))));

            SetContentView(root);
            SetApplyCancelEnabled(false);
        }

        private HorizontalScrollView WrapScroll(View child)
        {
            var scroll = new HorizontalScrollView(this);
            scroll.AddView(child);
            return scroll;
        }

        private LinearLayout CreateButtonRow(params (string Label, Action Action)[] items)
        {
            var row = new LinearLayout(this) { Orientation = ViewOrientation.Horizontal };
            foreach ((string label, Action action) in items)
            {
                var btn = new AButton(this) { Text = label };
                btn.SetAllCaps(false);
                btn.LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
                Action captured = action;
                btn.Click += (_, __) => captured();
                row.AddView(btn);
            }
            return row;
        }

        private void EnterMode(ToolMode mode)
        {
            _mode = mode;
            _canvas.Mode = mode;
            _canvas.ClearOverlay();
            SetApplyCancelEnabled(mode == ToolMode.Crop || mode == ToolMode.Blur
                || mode == ToolMode.Text || mode == ToolMode.Emoji || mode == ToolMode.Arrow);
            RefreshStatus();
        }

        private void CancelTool()
        {
            _mode = ToolMode.None;
            _canvas.Mode = ToolMode.None;
            _canvas.ClearOverlay();
            SetApplyCancelEnabled(false);
            RefreshStatus();
        }

        private void SetApplyCancelEnabled(bool enabled)
        {
            if (_applyBtn != null)
                _applyBtn.Enabled = enabled;
            if (_cancelToolBtn != null)
                _cancelToolBtn.Enabled = enabled;
        }

        private void OnSelectionFinished()
        {
            // Crop/Blur selection ready — wait for Apply.
            RefreshStatus();
        }

        private void OnBrushStrokeFinished(IList<PointF> points, float strokeWidth, Color color)
        {
            if (_bitmap == null || points == null || points.Count < 2)
                return;

            PushHistory();
            using (var canvas = new Canvas(_bitmap))
            using (var paint = new Paint { AntiAlias = true, StrokeWidth = strokeWidth, Color = color })
            {
                paint.SetStyle(Paint.Style.Stroke);
                paint.StrokeCap = Paint.Cap.Round;
                paint.StrokeJoin = Paint.Join.Round;
                var path = new Path();
                path.MoveTo(points[0].X, points[0].Y);
                for (int i = 1; i < points.Count; i++)
                    path.LineTo(points[i].X, points[i].Y);
                canvas.DrawPath(path, paint);
            }
            _canvas.SetBitmap(_bitmap);
            _canvas.ClearOverlay();
            RefreshHeader();
            RefreshStatus();
            UpdateHistoryButtons();
        }

        private void ApplyPendingTool()
        {
            if (_bitmap == null)
                return;

            switch (_mode)
            {
                case ToolMode.Crop:
                    ApplyCrop();
                    break;
                case ToolMode.Blur:
                    ApplyBlur();
                    break;
                case ToolMode.Text:
                    ApplyTextOverlay();
                    break;
                case ToolMode.Emoji:
                    ApplyEmojiOverlay();
                    break;
                case ToolMode.Arrow:
                    ApplyArrowOverlay();
                    break;
            }
        }

        private void ApplyCrop()
        {
            RectF? rect = _canvas.GetImageSelectionRect();
            if (rect == null)
            {
                Toast.MakeText(this, "Drag a crop area first", ToastLength.Short).Show();
                return;
            }

            RectF r = rect.Value;
            int x = Math.Max(0, (int)Math.Floor(r.Left));
            int y = Math.Max(0, (int)Math.Floor(r.Top));
            int w = Math.Max(1, (int)Math.Ceiling(r.Width()));
            int h = Math.Max(1, (int)Math.Ceiling(r.Height()));
            if (x + w > _bitmap.Width)
                w = _bitmap.Width - x;
            if (y + h > _bitmap.Height)
                h = _bitmap.Height - y;
            if (w < 1 || h < 1)
                return;

            PushHistory();
            Bitmap cropped = Bitmap.CreateBitmap(_bitmap, x, y, w, h);
            ReplaceBitmap(cropped);
            CancelTool();
        }

        private void ApplyBlur()
        {
            RectF? rect = _canvas.GetImageSelectionRect();
            if (rect == null)
            {
                Toast.MakeText(this, "Drag a blur area first", ToastLength.Short).Show();
                return;
            }

            RectF r = rect.Value;
            int x = Math.Max(0, (int)Math.Floor(r.Left));
            int y = Math.Max(0, (int)Math.Floor(r.Top));
            int w = Math.Max(1, (int)Math.Ceiling(r.Width()));
            int h = Math.Max(1, (int)Math.Ceiling(r.Height()));
            if (x + w > _bitmap.Width)
                w = _bitmap.Width - x;
            if (y + h > _bitmap.Height)
                h = _bitmap.Height - y;
            if (w < 2 || h < 2)
                return;

            PushHistory();
            using Bitmap region = Bitmap.CreateBitmap(_bitmap, x, y, w, h);
            using Bitmap blurred = BoxBlur(region, radius: 8);
            using (var canvas = new Canvas(_bitmap))
            {
                canvas.DrawBitmap(blurred, x, y, null);
            }
            _canvas.SetBitmap(_bitmap);
            CancelTool();
            RefreshHeader();
            UpdateHistoryButtons();
        }

        private void ApplyTextOverlay()
        {
            PointF? pos = _canvas.GetPlacementPoint();
            if (pos == null || string.IsNullOrWhiteSpace(_pendingText))
            {
                Toast.MakeText(this, "Tap to place text first", ToastLength.Short).Show();
                return;
            }

            PushHistory();
            using (var canvas = new Canvas(_bitmap))
            using (var paint = new Paint { AntiAlias = true, Color = Palette[_brushColorIndex], TextSize = Math.Max(12, _bitmap.Width / 18f) })
            {
                paint.SetTypeface(_pendingTypeface ?? Typeface.Default);
                canvas.DrawText(_pendingText, pos.Value.X, pos.Value.Y, paint);
            }
            _canvas.SetBitmap(_bitmap);
            CancelTool();
            RefreshHeader();
            UpdateHistoryButtons();
        }

        private void ApplyEmojiOverlay()
        {
            PointF? pos = _canvas.GetPlacementPoint();
            if (pos == null || string.IsNullOrEmpty(_pendingEmoji))
            {
                Toast.MakeText(this, "Tap to place emoji first", ToastLength.Short).Show();
                return;
            }

            float textSize = 24f + (_emojiSizeStep * 12f);
            PushHistory();
            using (var canvas = new Canvas(_bitmap))
            using (var paint = new Paint { AntiAlias = true, TextSize = textSize, Color = Color.White })
            {
                canvas.DrawText(_pendingEmoji, pos.Value.X, pos.Value.Y, paint);
            }
            _canvas.SetBitmap(_bitmap);
            CancelTool();
            RefreshHeader();
            UpdateHistoryButtons();
        }

        private void ApplyArrowOverlay()
        {
            (PointF Start, PointF End)? arrow = _canvas.GetArrowPoints();
            if (arrow == null)
            {
                Toast.MakeText(this, "Drag to draw an arrow first", ToastLength.Short).Show();
                return;
            }

            float stroke = 2f + (_arrowSizeStep * 2f);
            PushHistory();
            using (var canvas = new Canvas(_bitmap))
            using (var paint = new Paint { AntiAlias = true, StrokeWidth = stroke, Color = Palette[_arrowColorIndex] })
            {
                paint.SetStyle(Paint.Style.Stroke);
                paint.StrokeCap = Paint.Cap.Round;
                DrawArrow(canvas, paint, arrow.Value.Start, arrow.Value.End, stroke);
            }
            _canvas.SetBitmap(_bitmap);
            CancelTool();
            RefreshHeader();
            UpdateHistoryButtons();
        }

        private static void DrawArrow(Canvas canvas, Paint paint, PointF start, PointF end, float stroke)
        {
            canvas.DrawLine(start.X, start.Y, end.X, end.Y, paint);
            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            float head = Math.Max(12f, stroke * 3.5f);
            float x1 = end.X - (float)(head * Math.Cos(angle - Math.PI / 6));
            float y1 = end.Y - (float)(head * Math.Sin(angle - Math.PI / 6));
            float x2 = end.X - (float)(head * Math.Cos(angle + Math.PI / 6));
            float y2 = end.Y - (float)(head * Math.Sin(angle + Math.PI / 6));
            canvas.DrawLine(end.X, end.Y, x1, y1, paint);
            canvas.DrawLine(end.X, end.Y, x2, y2, paint);
        }

        private void Rotate90()
        {
            if (_bitmap == null)
                return;
            PushHistory();
            var matrix = new Matrix();
            matrix.PostRotate(90);
            Bitmap rotated = Bitmap.CreateBitmap(_bitmap, 0, 0, _bitmap.Width, _bitmap.Height, matrix, true);
            ReplaceBitmap(rotated);
            CancelTool();
        }

        private void ShrinkContent10()
        {
            if (_bitmap == null)
                return;
            int w = Math.Max(1, (int)(_bitmap.Width * 0.9f));
            int h = Math.Max(1, (int)(_bitmap.Height * 0.9f));
            PushHistory();
            Bitmap scaled = Bitmap.CreateScaledBitmap(_bitmap, w, h, true);
            ReplaceBitmap(scaled);
            CancelTool();
        }

        private void ExpandCanvas10()
        {
            if (_bitmap == null)
                return;

            int newW = Math.Max(_bitmap.Width + 2, (int)Math.Round(_bitmap.Width * 1.1));
            int newH = Math.Max(_bitmap.Height + 2, (int)Math.Round(_bitmap.Height * 1.1));
            int offsetX = (newW - _bitmap.Width) / 2;
            int offsetY = (newH - _bitmap.Height) / 2;

            PushHistory();
            Bitmap expanded = Bitmap.CreateBitmap(newW, newH, Bitmap.Config.Argb8888);
            expanded.EraseColor(Color.Black);
            using (var canvas = new Canvas(expanded))
            {
                canvas.DrawBitmap(_bitmap, offsetX, offsetY, null);
            }
            ReplaceBitmap(expanded);
            CancelTool();
        }

        private void ShowBrushOptions()
        {
            var sizeSeek = new SeekBar(this) { Max = 19, Progress = Math.Max(0, _brushSize - 1) };
            var sizeLabel = new TextView(this) { Text = $"Brush size: {_brushSize}" };
            sizeLabel.SetTextColor(Color.White);
            sizeSeek.ProgressChanged += (_, e) =>
            {
                if (!e.FromUser)
                    return;
                _brushSize = e.Progress + 1;
                sizeLabel.Text = $"Brush size: {_brushSize}";
            };

            var colorSpinner = CreateColorSpinner(_brushColorIndex);
            var layout = new LinearLayout(this) { Orientation = ViewOrientation.Vertical };
            layout.SetPadding(40, 20, 40, 10);
            layout.AddView(sizeLabel);
            layout.AddView(sizeSeek);
            layout.AddView(Label("Color"));
            layout.AddView(colorSpinner);

            new AlertDialog.Builder(this)
                .SetTitle("Brush")
                .SetView(layout)
                .SetPositiveButton("OK", (_, __) =>
                {
                    _brushColorIndex = colorSpinner.SelectedItemPosition;
                    _canvas.BrushSize = _brushSize;
                    _canvas.BrushColor = Palette[_brushColorIndex];
                    EnterMode(ToolMode.Brush);
                    SetApplyCancelEnabled(false);
                })
                .SetNegativeButton("Cancel", (_, __) => { })
                .Show();
        }

        private void ShowTextOptions()
        {
            var input = new EditText(this) { Hint = "Text" };
            input.SetText(_pendingText, TextView.BufferType.Editable);

            string[] fonts = ListFonts();
            var fontSpinner = new Spinner(this);
            fontSpinner.Adapter = new ArrayAdapter(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, fonts);
            int fontIndex = Array.FindIndex(fonts, f => string.Equals(f, _pendingFontLabel, StringComparison.Ordinal));
            if (fontIndex >= 0)
                fontSpinner.SetSelection(fontIndex);

            var colorSpinner = CreateColorSpinner(_brushColorIndex);
            var layout = new LinearLayout(this) { Orientation = ViewOrientation.Vertical };
            layout.SetPadding(40, 20, 40, 10);
            layout.AddView(Label("Text"));
            layout.AddView(input);
            layout.AddView(Label("Font"));
            layout.AddView(fontSpinner);
            layout.AddView(Label("Color"));
            layout.AddView(colorSpinner);

            new AlertDialog.Builder(this)
                .SetTitle("Text")
                .SetView(layout)
                .SetPositiveButton("OK", (_, __) =>
                {
                    _pendingText = input.Text ?? string.Empty;
                    _pendingFontLabel = fonts[Math.Max(0, fontSpinner.SelectedItemPosition)];
                    _pendingTypeface = ResolveTypeface(_pendingFontLabel);
                    _brushColorIndex = colorSpinner.SelectedItemPosition;
                    EnterMode(ToolMode.Text);
                    Toast.MakeText(this, "Tap image to place text, then Apply", ToastLength.Short).Show();
                })
                .SetNegativeButton("Cancel", (_, __) => { })
                .Show();
        }

        private void ShowEmojiOptions()
        {
            string[] emojis =
            {
                "😀", "😂", "😍", "🤔", "😎", "😭", "🔥", "⭐", "❤️", "👍",
                "🎉", "🚀", "👀", "🙏", "💯", "✨", "📷", "✅", "❌", "➡️",
            };

            var emojiSpinner = new Spinner(this);
            emojiSpinner.Adapter = new ArrayAdapter(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, emojis);
            int emojiIndex = Array.IndexOf(emojis, _pendingEmoji);
            if (emojiIndex >= 0)
                emojiSpinner.SetSelection(emojiIndex);

            var custom = new EditText(this) { Hint = "Or type/paste any emoji" };
            custom.SetText(_pendingEmoji, TextView.BufferType.Editable);

            var sizeSeek = new SeekBar(this) { Max = 9, Progress = Math.Max(0, _emojiSizeStep - 1) };
            var sizeLabel = new TextView(this) { Text = $"Size step: {_emojiSizeStep}/10" };
            sizeLabel.SetTextColor(Color.White);
            sizeSeek.ProgressChanged += (_, e) =>
            {
                if (!e.FromUser)
                    return;
                _emojiSizeStep = e.Progress + 1;
                sizeLabel.Text = $"Size step: {_emojiSizeStep}/10";
            };

            var layout = new LinearLayout(this) { Orientation = ViewOrientation.Vertical };
            layout.SetPadding(40, 20, 40, 10);
            layout.AddView(Label("Emoji"));
            layout.AddView(emojiSpinner);
            layout.AddView(custom);
            layout.AddView(sizeLabel);
            layout.AddView(sizeSeek);

            new AlertDialog.Builder(this)
                .SetTitle("Emoji")
                .SetView(layout)
                .SetPositiveButton("OK", (_, __) =>
                {
                    string typed = (custom.Text ?? string.Empty).Trim();
                    _pendingEmoji = !string.IsNullOrEmpty(typed)
                        ? typed
                        : emojis[Math.Max(0, emojiSpinner.SelectedItemPosition)];
                    EnterMode(ToolMode.Emoji);
                    Toast.MakeText(this, "Tap image to place emoji, then Apply", ToastLength.Short).Show();
                })
                .SetNegativeButton("Cancel", (_, __) => { })
                .Show();
        }

        private void ShowArrowOptions()
        {
            var sizeSeek = new SeekBar(this) { Max = 9, Progress = Math.Max(0, _arrowSizeStep - 1) };
            var sizeLabel = new TextView(this) { Text = $"Size step: {_arrowSizeStep}/10" };
            sizeLabel.SetTextColor(Color.White);
            sizeSeek.ProgressChanged += (_, e) =>
            {
                if (!e.FromUser)
                    return;
                _arrowSizeStep = e.Progress + 1;
                sizeLabel.Text = $"Size step: {_arrowSizeStep}/10";
            };

            var colorSpinner = CreateColorSpinner(_arrowColorIndex);
            var layout = new LinearLayout(this) { Orientation = ViewOrientation.Vertical };
            layout.SetPadding(40, 20, 40, 10);
            layout.AddView(sizeLabel);
            layout.AddView(sizeSeek);
            layout.AddView(Label("Color"));
            layout.AddView(colorSpinner);

            new AlertDialog.Builder(this)
                .SetTitle("Arrow")
                .SetView(layout)
                .SetPositiveButton("OK", (_, __) =>
                {
                    _arrowColorIndex = colorSpinner.SelectedItemPosition;
                    _canvas.ArrowColor = Palette[_arrowColorIndex];
                    _canvas.ArrowStroke = 2f + (_arrowSizeStep * 2f);
                    EnterMode(ToolMode.Arrow);
                    Toast.MakeText(this, "Drag to draw arrow, drag ends to reposition, then Apply", ToastLength.Short).Show();
                })
                .SetNegativeButton("Cancel", (_, __) => { })
                .Show();
        }

        private void ShowConvertDialog()
        {
            string[] formats = { "jpg", "png", "webp" };
            var formatSpinner = new Spinner(this);
            formatSpinner.Adapter = new ArrayAdapter(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, formats);
            string current = _outputFormat.TrimStart('.').ToLowerInvariant();
            int idx = Array.IndexOf(formats, current);
            formatSpinner.SetSelection(idx >= 0 ? idx : 0);

            var qualitySeek = new SeekBar(this) { Max = 100, Progress = _outputQuality };
            var qualityLabel = new TextView(this) { Text = $"Quality: {_outputQuality}%" };
            qualityLabel.SetTextColor(Color.White);
            qualitySeek.ProgressChanged += (_, e) =>
            {
                qualityLabel.Text = $"Quality: {e.Progress}%";
            };

            var layout = new LinearLayout(this) { Orientation = ViewOrientation.Vertical };
            layout.SetPadding(40, 20, 40, 10);
            layout.AddView(Label("Format"));
            layout.AddView(formatSpinner);
            layout.AddView(qualityLabel);
            layout.AddView(qualitySeek);

            new AlertDialog.Builder(this)
                .SetTitle("Convert")
                .SetView(layout)
                .SetPositiveButton("Apply", (_, __) =>
                {
                    _outputFormat = "." + formats[Math.Max(0, formatSpinner.SelectedItemPosition)];
                    _outputQuality = Math.Max(1, qualitySeek.Progress);
                    RefreshHeader();
                    RefreshStatus();
                    Toast.MakeText(this, $"Will save as {_outputFormat} @ {_outputQuality}%", ToastLength.Short).Show();
                })
                .SetNegativeButton("Cancel", (_, __) => { })
                .Show();
        }

        private void ClearExifNow()
        {
            _clearAllExif = true;
            _exifDateTime = DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
            RefreshStatus();
            Toast.MakeText(this, "EXIF will be cleared; datetime set to now on save", ToastLength.Short).Show();
        }

        private void PromptSaveAs()
        {
            string dir = IOPath.GetDirectoryName(_path) ?? string.Empty;
            string baseName = IOPath.GetFileNameWithoutExtension(_path) ?? "image";
            var input = new EditText(this);
            input.SetText(baseName + "_edited" + _outputFormat, TextView.BufferType.Editable);

            new AlertDialog.Builder(this)
                .SetTitle("Save as")
                .SetMessage(dir)
                .SetView(input)
                .SetPositiveButton("Save", (_, __) =>
                {
                    string name = (input.Text ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(name))
                    {
                        Toast.MakeText(this, "Name required", ToastLength.Short).Show();
                        return;
                    }
                    if (!name.Contains('.', StringComparison.Ordinal))
                        name += _outputFormat;
                    string target = IOPath.Combine(dir, name);
                    SaveToPath(target, updateCurrentPath: true);
                })
                .SetNegativeButton("Cancel", (_, __) => { })
                .Show();
        }

        private void Save(bool overwrite)
        {
            if (!overwrite)
            {
                PromptSaveAs();
                return;
            }
            SaveToPath(_path, updateCurrentPath: true);
        }

        private void SaveToPath(string targetPath, bool updateCurrentPath)
        {
            try
            {
                if (_bitmap == null)
                {
                    Toast.MakeText(this, "Nothing to save", ToastLength.Short).Show();
                    return;
                }

                string ext = NormalizeExt(IOPath.GetExtension(targetPath));
                if (ext != _outputFormat)
                {
                    // Prefer convert format when user asked to convert.
                    string newPath = IOPath.ChangeExtension(targetPath, _outputFormat);
                    if (!string.Equals(newPath, targetPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(targetPath, _path, StringComparison.OrdinalIgnoreCase))
                    {
                        // Overwrite original path but with new extension: write new file and delete old if different.
                        targetPath = newPath;
                    }
                    else if (!string.Equals(IOPath.GetExtension(targetPath), _outputFormat, StringComparison.OrdinalIgnoreCase))
                    {
                        targetPath = IOPath.ChangeExtension(targetPath, _outputFormat);
                    }
                    ext = _outputFormat;
                }

                Bitmap.CompressFormat format = ext switch
                {
                    ".png" => Bitmap.CompressFormat.Png,
                    ".webp" => (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                        ? Bitmap.CompressFormat.WebpLossy
                        : Bitmap.CompressFormat.Webp,
                    _ => Bitmap.CompressFormat.Jpeg,
                };

                string temp = targetPath + ".edit.tmp";
                string targetDir = IOPath.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                using (var stream = File.Open(temp, FileMode.Create, FileAccess.Write))
                {
                    int quality = ext == ".png" ? 100 : Math.Clamp(_outputQuality, 1, 100);
                    _bitmap.Compress(format, quality, stream);
                }

                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(temp, targetPath);

                // If convert-on-overwrite changed extension, remove old path.
                if (updateCurrentPath
                    && !string.Equals(_path, targetPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(_path)
                    && string.Equals(IOPath.GetDirectoryName(_path), IOPath.GetDirectoryName(targetPath), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(IOPath.GetFileNameWithoutExtension(_path), IOPath.GetFileNameWithoutExtension(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(_path); } catch { }
                }

                WriteExif(targetPath);

                if (updateCurrentPath)
                    _path = targetPath;

                RefreshHeader();
                Toast.MakeText(this, "Saved", ToastLength.Short).Show();
                SetResult(Result.Ok);
                if (updateCurrentPath && string.Equals(targetPath, _path, StringComparison.OrdinalIgnoreCase))
                {
                    // Keep editor open after save so further edits are possible; only finish on Save overwrite from old UX?
                    // User asked for Save (overwrite) — keep open so Undo still works on memory.
                }
            }
            catch (Exception ex)
            {
                Toast.MakeText(this, ex.Message, ToastLength.Long).Show();
            }
        }

        private void WriteExif(string path)
        {
            try
            {
                if (_clearAllExif)
                {
                    // Re-encode already stripped most EXIF; write a fresh datetime-only EXIF.
                    var exif = new ExifInterface(path);
                    ClearAllExifAttributes(exif);
                    if (!string.IsNullOrEmpty(_exifDateTime))
                    {
                        exif.SetAttribute(ExifInterface.TagDatetime, _exifDateTime);
                        exif.SetAttribute(ExifInterface.TagDatetimeOriginal, _exifDateTime);
                        exif.SetAttribute(ExifInterface.TagDatetimeDigitized, _exifDateTime);
                    }
                    exif.SetAttribute(ExifInterface.TagOrientation, ((int)MediaOrientation.Normal).ToString(CultureInfo.InvariantCulture));
                    exif.SaveAttributes();
                    return;
                }

                if (!string.IsNullOrEmpty(_exifDateTime))
                {
                    var exif = new ExifInterface(path);
                    exif.SetAttribute(ExifInterface.TagDatetime, _exifDateTime);
                    exif.SetAttribute(ExifInterface.TagDatetimeOriginal, _exifDateTime);
                    exif.SetAttribute(ExifInterface.TagOrientation, ((int)MediaOrientation.Normal).ToString(CultureInfo.InvariantCulture));
                    exif.SaveAttributes();
                }
            }
            catch
            {
            }
        }

        private static void ClearAllExifAttributes(ExifInterface exif)
        {
            // Re-encoding already drops most metadata; clear common leftover tags explicitly.
            string[] tags =
            {
                ExifInterface.TagArtist, ExifInterface.TagCopyright, ExifInterface.TagDatetime,
                ExifInterface.TagDatetimeDigitized, ExifInterface.TagDatetimeOriginal,
                ExifInterface.TagGpsAltitude, ExifInterface.TagGpsAltitudeRef,
                ExifInterface.TagGpsDatestamp, ExifInterface.TagGpsLatitude, ExifInterface.TagGpsLatitudeRef,
                ExifInterface.TagGpsLongitude, ExifInterface.TagGpsLongitudeRef,
                ExifInterface.TagGpsProcessingMethod, ExifInterface.TagGpsTimestamp,
                ExifInterface.TagImageDescription, ExifInterface.TagMake, ExifInterface.TagModel,
                ExifInterface.TagSoftware, ExifInterface.TagUserComment, ExifInterface.TagOrientation,
                ExifInterface.TagLensMake, ExifInterface.TagLensModel, ExifInterface.TagFocalLength,
                ExifInterface.TagFNumber, ExifInterface.TagExposureTime, ExifInterface.TagFlash,
                ExifInterface.TagWhiteBalance, ExifInterface.TagSubsecTime, ExifInterface.TagSubsecTimeOriginal,
                ExifInterface.TagSubsecTimeDigitized,
            };

            foreach (string tag in tags)
            {
                try { exif.SetAttribute(tag, null); } catch { }
            }
        }

        private void PushHistory()
        {
            if (_bitmap == null)
                return;
            _undo.Push(_bitmap.Copy(_bitmap.GetConfig() ?? Bitmap.Config.Argb8888, true));
            if (_undo.Count > MaxHistory)
            {
                // Stack enumerates newest → oldest; drop the oldest entry.
                Bitmap[] items = _undo.ToArray();
                try { items[^1]?.Recycle(); } catch { }
                _undo.Clear();
                for (int i = items.Length - 2; i >= 0; i--)
                    _undo.Push(items[i]);
            }
            RecycleStack(_redo);
            UpdateHistoryButtons();
        }

        private void Undo()
        {
            if (_undo.Count == 0 || _bitmap == null)
                return;
            _redo.Push(_bitmap);
            _bitmap = _undo.Pop();
            _canvas.SetBitmap(_bitmap);
            CancelTool();
            RefreshHeader();
            UpdateHistoryButtons();
        }

        private void Redo()
        {
            if (_redo.Count == 0)
                return;
            if (_bitmap != null)
                _undo.Push(_bitmap);
            _bitmap = _redo.Pop();
            _canvas.SetBitmap(_bitmap);
            CancelTool();
            RefreshHeader();
            UpdateHistoryButtons();
        }

        private void ReplaceBitmap(Bitmap next)
        {
            try { _bitmap?.Recycle(); } catch { }
            _bitmap = next;
            _canvas.SetBitmap(_bitmap);
            RefreshHeader();
            RefreshStatus();
            UpdateHistoryButtons();
        }

        private void UpdateHistoryButtons()
        {
            if (_undoBtn != null)
                _undoBtn.Enabled = _undo.Count > 0;
            if (_redoBtn != null)
                _redoBtn.Enabled = _redo.Count > 0;
        }

        private void RefreshHeader()
        {
            if (_headerInfo == null)
                return;

            string sizeText = "?";
            try
            {
                if (File.Exists(_path))
                {
                    long bytes = new FileInfo(_path).Length;
                    sizeText = FormatBytes(bytes);
                }
            }
            catch
            {
            }

            string dims = _bitmap == null ? "" : $"{_bitmap.Width}×{_bitmap.Height}";
            _headerInfo.Text = $"{sizeText}  {dims}\n{_path}";
        }

        private void RefreshStatus()
        {
            if (_status == null)
                return;

            string mode = _mode == ToolMode.None ? "ready" : _mode.ToString().ToLowerInvariant();
            string exif = _clearAllExif
                ? $"clear → {_exifDateTime}"
                : (string.IsNullOrEmpty(_exifDateTime) ? "—" : _exifDateTime);
            _status.Text = $"mode: {mode} · format {_outputFormat} @ {_outputQuality}% · EXIF {exif}";
        }

        private void LoadExifDate()
        {
            try
            {
                var exif = new ExifInterface(_path);
                _exifDateTime = exif.GetAttribute(ExifInterface.TagDatetime) ?? string.Empty;
            }
            catch
            {
                _exifDateTime = string.Empty;
            }
        }

        private TextView Label(string text)
        {
            var tv = new TextView(this) { Text = text, TextSize = 13 };
            tv.SetTextColor(Color.LightGray);
            tv.SetPadding(0, 12, 0, 4);
            return tv;
        }

        private Spinner CreateColorSpinner(int selected)
        {
            string[] names = { "White", "Black", "Red", "Yellow", "Green", "Cyan", "Blue", "Magenta", "Orange", "Gray" };
            var spinner = new Spinner(this);
            spinner.Adapter = new ArrayAdapter(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, names);
            spinner.SetSelection(Math.Clamp(selected, 0, names.Length - 1));
            return spinner;
        }

        private string[] ListFonts()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(AbroFontsDir))
                {
                    foreach (string file in Directory.EnumerateFiles(AbroFontsDir)
                        .Where(f =>
                        {
                            string e = IOPath.GetExtension(f)?.ToLowerInvariant();
                            return e is ".ttf" or ".otf" or ".ttc";
                        })
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(IOPath.GetFileName(file));
                    }
                }
            }
            catch
            {
            }

            if (list.Count == 0)
            {
                list.Add("Default");
                list.Add("Sans Serif");
                list.Add("Serif");
                list.Add("Monospace");
                try
                {
                    const string systemFonts = "/system/fonts";
                    if (Directory.Exists(systemFonts))
                    {
                        foreach (string file in Directory.EnumerateFiles(systemFonts, "*.ttf").Take(30))
                            list.Add("system:" + IOPath.GetFileName(file));
                    }
                }
                catch
                {
                }
            }

            return list.ToArray();
        }

        private Typeface ResolveTypeface(string label)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(label) || label == "Default")
                    return Typeface.Default;
                if (label == "Sans Serif")
                    return Typeface.SansSerif;
                if (label == "Serif")
                    return Typeface.Serif;
                if (label == "Monospace")
                    return Typeface.Monospace;

                if (label.StartsWith("system:", StringComparison.Ordinal))
                {
                    string path = IOPath.Combine("/system/fonts", label.Substring("system:".Length));
                    if (File.Exists(path))
                        return Typeface.CreateFromFile(path);
                }

                string abroPath = IOPath.Combine(AbroFontsDir, label);
                if (File.Exists(abroPath))
                    return Typeface.CreateFromFile(abroPath);
            }
            catch
            {
            }
            return Typeface.Default;
        }

        private static string NormalizeExt(string ext)
        {
            ext = (ext ?? ".jpg").ToLowerInvariant();
            return ext switch
            {
                ".jpeg" => ".jpg",
                ".jpg" or ".png" or ".webp" => ext,
                _ => ".jpg",
            };
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024)
                return kb.ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            double mb = kb / 1024.0;
            return mb.ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }

        private static void RecycleStack(Stack<Bitmap> stack)
        {
            while (stack.Count > 0)
            {
                try { stack.Pop()?.Recycle(); } catch { }
            }
        }

        private static Bitmap BoxBlur(Bitmap source, int radius)
        {
            radius = Math.Clamp(radius, 1, 25);
            int w = source.Width;
            int h = source.Height;
            int[] pix = new int[w * h];
            source.GetPixels(pix, 0, w, 0, 0, w, h);
            int[] temp = new int[w * h];

            int div = radius + radius + 1;
            for (int y = 0; y < h; y++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    int p = pix[y * w + Math.Clamp(i, 0, w - 1)];
                    a += (p >> 24) & 0xff;
                    r += (p >> 16) & 0xff;
                    g += (p >> 8) & 0xff;
                    b += p & 0xff;
                }
                for (int x = 0; x < w; x++)
                {
                    temp[y * w + x] =
                        ((a / div) << 24) |
                        ((r / div) << 16) |
                        ((g / div) << 8) |
                        (b / div);
                    int p1 = pix[y * w + Math.Clamp(x - radius, 0, w - 1)];
                    int p2 = pix[y * w + Math.Clamp(x + radius + 1, 0, w - 1)];
                    a += ((p2 >> 24) & 0xff) - ((p1 >> 24) & 0xff);
                    r += ((p2 >> 16) & 0xff) - ((p1 >> 16) & 0xff);
                    g += ((p2 >> 8) & 0xff) - ((p1 >> 8) & 0xff);
                    b += (p2 & 0xff) - (p1 & 0xff);
                }
            }

            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    int p = temp[Math.Clamp(i, 0, h - 1) * w + x];
                    a += (p >> 24) & 0xff;
                    r += (p >> 16) & 0xff;
                    g += (p >> 8) & 0xff;
                    b += p & 0xff;
                }
                for (int y = 0; y < h; y++)
                {
                    pix[y * w + x] =
                        ((a / div) << 24) |
                        ((r / div) << 16) |
                        ((g / div) << 8) |
                        (b / div);
                    int p1 = temp[Math.Clamp(y - radius, 0, h - 1) * w + x];
                    int p2 = temp[Math.Clamp(y + radius + 1, 0, h - 1) * w + x];
                    a += ((p2 >> 24) & 0xff) - ((p1 >> 24) & 0xff);
                    r += ((p2 >> 16) & 0xff) - ((p1 >> 16) & 0xff);
                    g += ((p2 >> 8) & 0xff) - ((p1 >> 8) & 0xff);
                    b += (p2 & 0xff) - (p1 & 0xff);
                }
            }

            Bitmap result = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888);
            result.SetPixels(pix, 0, w, 0, 0, w, h);
            return result;
        }

        public static Intent CreateIntent(Context context, string path)
        {
            var intent = new Intent(context, typeof(ImageEditorActivity));
            intent.PutExtra(ExtraPath, path);
            return intent;
        }

        /// <summary>
        /// Canvas that maps touch to image coordinates and supports crop/brush/blur/text/emoji/arrow overlays.
        /// </summary>
        private sealed class EditorCanvasView : View
        {
            private Bitmap _bitmap;
            private readonly Paint _bitmapPaint = new Paint { FilterBitmap = true, AntiAlias = true };
            private readonly Paint _overlayPaint = new Paint { AntiAlias = true };
            private readonly Paint _handlePaint = new Paint { AntiAlias = true };

            private RectF _dst = new RectF();
            private float _scale = 1f;
            private float _offsetX;
            private float _offsetY;

            private bool _dragging;
            private float _startX;
            private float _startY;
            private float _curX;
            private float _curY;
            private RectF _selection = new RectF();
            private bool _hasSelection;

            private readonly List<PointF> _stroke = new List<PointF>();
            private PointF? _placement;
            private PointF? _arrowStart;
            private PointF? _arrowEnd;
            private int _arrowDragTarget; // 0 none, 1 start, 2 end, 3 whole create

            public ToolMode Mode { get; set; } = ToolMode.None;
            public float BrushSize { get; set; } = 6f;
            public Color BrushColor { get; set; } = Color.Red;
            public float ArrowStroke { get; set; } = 8f;
            public Color ArrowColor { get; set; } = Color.Yellow;

            public event Action SelectionFinished;
            public event Action<IList<PointF>, float, Color> StrokeFinished;

            public EditorCanvasView(Context context)
                : base(context)
            {
                SetBackgroundColor(Color.Rgb(20, 20, 20));
                _overlayPaint.SetStyle(Paint.Style.Stroke);
                _overlayPaint.StrokeWidth = 3f;
                _overlayPaint.Color = Color.Argb(220, 36, 129, 204);
                _handlePaint.SetStyle(Paint.Style.Fill);
                _handlePaint.Color = Color.White;
            }

            public void SetBitmap(Bitmap bitmap)
            {
                _bitmap = bitmap;
                ComputeTransform();
                Invalidate();
            }

            public void ClearOverlay()
            {
                _hasSelection = false;
                _selection.SetEmpty();
                _stroke.Clear();
                _placement = null;
                _arrowStart = null;
                _arrowEnd = null;
                _dragging = false;
                Invalidate();
            }

            public RectF? GetImageSelectionRect()
            {
                if (!_hasSelection || _bitmap == null)
                    return null;
                float left = Math.Min(_selection.Left, _selection.Right);
                float top = Math.Min(_selection.Top, _selection.Bottom);
                float right = Math.Max(_selection.Left, _selection.Right);
                float bottom = Math.Max(_selection.Top, _selection.Bottom);
                if (right - left < 2 || bottom - top < 2)
                    return null;
                return new RectF(
                    Math.Clamp(left, 0, _bitmap.Width),
                    Math.Clamp(top, 0, _bitmap.Height),
                    Math.Clamp(right, 0, _bitmap.Width),
                    Math.Clamp(bottom, 0, _bitmap.Height));
            }

            public PointF? GetPlacementPoint() => _placement;

            public (PointF Start, PointF End)? GetArrowPoints()
            {
                if (_arrowStart == null || _arrowEnd == null)
                    return null;
                return (_arrowStart.Value, _arrowEnd.Value);
            }

            protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
            {
                base.OnSizeChanged(w, h, oldw, oldh);
                ComputeTransform();
            }

            private void ComputeTransform()
            {
                if (_bitmap == null || Width <= 0 || Height <= 0)
                    return;

                float sx = (float)Width / _bitmap.Width;
                float sy = (float)Height / _bitmap.Height;
                _scale = Math.Min(sx, sy);
                float dw = _bitmap.Width * _scale;
                float dh = _bitmap.Height * _scale;
                _offsetX = (Width - dw) / 2f;
                _offsetY = (Height - dh) / 2f;
                _dst.Set(_offsetX, _offsetY, _offsetX + dw, _offsetY + dh);
            }

            private PointF? ScreenToImage(float x, float y)
            {
                if (_bitmap == null || _scale <= 0)
                    return null;
                if (x < _dst.Left || x > _dst.Right || y < _dst.Top || y > _dst.Bottom)
                {
                    // Clamp into image for easier touch near edges.
                    x = Math.Clamp(x, _dst.Left, _dst.Right);
                    y = Math.Clamp(y, _dst.Top, _dst.Bottom);
                }
                return new PointF((x - _offsetX) / _scale, (y - _offsetY) / _scale);
            }

            private PointF ImageToScreen(PointF p) =>
                new PointF(p.X * _scale + _offsetX, p.Y * _scale + _offsetY);

            public override bool OnTouchEvent(MotionEvent e)
            {
                if (_bitmap == null || Mode == ToolMode.None)
                    return false;

                PointF? img = ScreenToImage(e.GetX(), e.GetY());
                if (img == null)
                    return true;

                switch (Mode)
                {
                    case ToolMode.Crop:
                    case ToolMode.Blur:
                        HandleSelectionTouch(e, img.Value);
                        break;
                    case ToolMode.Brush:
                        HandleBrushTouch(e, img.Value);
                        break;
                    case ToolMode.Text:
                    case ToolMode.Emoji:
                        HandlePlacementTouch(e, img.Value);
                        break;
                    case ToolMode.Arrow:
                        HandleArrowTouch(e, img.Value);
                        break;
                }

                return true;
            }

            private void HandleSelectionTouch(MotionEvent e, PointF img)
            {
                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _dragging = true;
                        _startX = img.X;
                        _startY = img.Y;
                        _curX = img.X;
                        _curY = img.Y;
                        _hasSelection = true;
                        _selection.Set(_startX, _startY, _curX, _curY);
                        Invalidate();
                        break;
                    case MotionEventActions.Move:
                        if (!_dragging)
                            return;
                        _curX = img.X;
                        _curY = img.Y;
                        _selection.Set(_startX, _startY, _curX, _curY);
                        Invalidate();
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _dragging = false;
                        SelectionFinished?.Invoke();
                        Invalidate();
                        break;
                }
            }

            private void HandleBrushTouch(MotionEvent e, PointF img)
            {
                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _stroke.Clear();
                        _stroke.Add(img);
                        _dragging = true;
                        Invalidate();
                        break;
                    case MotionEventActions.Move:
                        if (!_dragging)
                            return;
                        _stroke.Add(img);
                        Invalidate();
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _dragging = false;
                        if (_stroke.Count >= 2)
                            StrokeFinished?.Invoke(_stroke.ToList(), BrushSize, BrushColor);
                        _stroke.Clear();
                        Invalidate();
                        break;
                }
            }

            private void HandlePlacementTouch(MotionEvent e, PointF img)
            {
                if (e.ActionMasked == MotionEventActions.Down
                    || e.ActionMasked == MotionEventActions.Move)
                {
                    _placement = img;
                    Invalidate();
                }
            }

            private void HandleArrowTouch(MotionEvent e, PointF img)
            {
                const float hit = 48f;
                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _dragging = true;
                        if (_arrowStart != null && _arrowEnd != null)
                        {
                            PointF s = ImageToScreen(_arrowStart.Value);
                            PointF en = ImageToScreen(_arrowEnd.Value);
                            float ds = Dist(e.GetX(), e.GetY(), s.X, s.Y);
                            float de = Dist(e.GetX(), e.GetY(), en.X, en.Y);
                            if (ds <= hit || de <= hit)
                            {
                                _arrowDragTarget = ds <= de ? 1 : 2;
                                break;
                            }
                        }
                        _arrowDragTarget = 3;
                        _arrowStart = img;
                        _arrowEnd = img;
                        Invalidate();
                        break;
                    case MotionEventActions.Move:
                        if (!_dragging)
                            return;
                        if (_arrowDragTarget == 1)
                            _arrowStart = img;
                        else if (_arrowDragTarget == 2)
                            _arrowEnd = img;
                        else
                            _arrowEnd = img;
                        Invalidate();
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _dragging = false;
                        _arrowDragTarget = 0;
                        Invalidate();
                        break;
                }
            }

            private static float Dist(float x1, float y1, float x2, float y2)
            {
                float dx = x1 - x2;
                float dy = y1 - y2;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            protected override void OnDraw(Canvas canvas)
            {
                base.OnDraw(canvas);
                if (_bitmap == null)
                    return;

                ComputeTransform();
                canvas.DrawBitmap(_bitmap, null, _dst, _bitmapPaint);

                if (_hasSelection && (Mode == ToolMode.Crop || Mode == ToolMode.Blur))
                {
                    float l = Math.Min(_selection.Left, _selection.Right) * _scale + _offsetX;
                    float t = Math.Min(_selection.Top, _selection.Bottom) * _scale + _offsetY;
                    float r = Math.Max(_selection.Left, _selection.Right) * _scale + _offsetX;
                    float b = Math.Max(_selection.Top, _selection.Bottom) * _scale + _offsetY;
                    _overlayPaint.Color = Mode == ToolMode.Blur
                        ? Color.Argb(220, 180, 80, 255)
                        : Color.Argb(220, 36, 129, 204);
                    canvas.DrawRect(l, t, r, b, _overlayPaint);
                }

                if (Mode == ToolMode.Brush && _stroke.Count > 1)
                {
                    using var paint = new Paint { AntiAlias = true, StrokeWidth = BrushSize * _scale, Color = BrushColor };
                    paint.SetStyle(Paint.Style.Stroke);
                    paint.StrokeCap = Paint.Cap.Round;
                    paint.StrokeJoin = Paint.Join.Round;
                    var path = new Path();
                    PointF p0 = ImageToScreen(_stroke[0]);
                    path.MoveTo(p0.X, p0.Y);
                    for (int i = 1; i < _stroke.Count; i++)
                    {
                        PointF p = ImageToScreen(_stroke[i]);
                        path.LineTo(p.X, p.Y);
                    }
                    canvas.DrawPath(path, paint);
                }

                if (_placement != null && (Mode == ToolMode.Text || Mode == ToolMode.Emoji))
                {
                    PointF s = ImageToScreen(_placement.Value);
                    canvas.DrawCircle(s.X, s.Y, 10f, _handlePaint);
                    _overlayPaint.Color = Color.Argb(200, 255, 255, 255);
                    canvas.DrawCircle(s.X, s.Y, 12f, _overlayPaint);
                }

                if (Mode == ToolMode.Arrow && _arrowStart != null && _arrowEnd != null)
                {
                    PointF s = ImageToScreen(_arrowStart.Value);
                    PointF en = ImageToScreen(_arrowEnd.Value);
                    using var paint = new Paint { AntiAlias = true, StrokeWidth = Math.Max(2f, ArrowStroke * _scale), Color = ArrowColor };
                    paint.SetStyle(Paint.Style.Stroke);
                    paint.StrokeCap = Paint.Cap.Round;
                    canvas.DrawLine(s.X, s.Y, en.X, en.Y, paint);
                    double angle = Math.Atan2(en.Y - s.Y, en.X - s.X);
                    float head = Math.Max(14f, paint.StrokeWidth * 3.5f);
                    float x1 = en.X - (float)(head * Math.Cos(angle - Math.PI / 6));
                    float y1 = en.Y - (float)(head * Math.Sin(angle - Math.PI / 6));
                    float x2 = en.X - (float)(head * Math.Cos(angle + Math.PI / 6));
                    float y2 = en.Y - (float)(head * Math.Sin(angle + Math.PI / 6));
                    canvas.DrawLine(en.X, en.Y, x1, y1, paint);
                    canvas.DrawLine(en.X, en.Y, x2, y2, paint);
                    canvas.DrawCircle(s.X, s.Y, 8f, _handlePaint);
                    canvas.DrawCircle(en.X, en.Y, 8f, _handlePaint);
                }
            }
        }
    }
}
