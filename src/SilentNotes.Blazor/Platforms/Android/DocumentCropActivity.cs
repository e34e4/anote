// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AButton = Android.Widget.Button;
using APaint = Android.Graphics.Paint;
using APath = Android.Graphics.Path;
using APointF = Android.Graphics.PointF;
using AView = Android.Views.View;
using Color = Android.Graphics.Color;
using IOPath = System.IO.Path;
using ViewOrientation = Android.Widget.Orientation;

namespace SilentNotes
{
    /// <summary>
    /// Four-corner perspective crop for document scanning (OCR prep).
    /// </summary>
    [Activity(
        Label = "aNote Document Crop",
        Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
        Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class DocumentCropActivity : Activity
    {
        public const string ExtraPath = "silentnotes.documentcrop.path";
        public const string ExtraResultPath = "silentnotes.documentcrop.result_path";

        private const float DefaultInsetFraction = 0.08f;

        private Bitmap _bitmap;
        private DocumentCropView _cropView;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            string path = Intent?.GetStringExtra(ExtraPath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Toast.MakeText(this, "Image not found", ToastLength.Short).Show();
                Finish();
                return;
            }

            try
            {
                _bitmap = BitmapFactory.DecodeFile(path)?.Copy(Bitmap.Config.Argb8888, false);
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
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            try
            {
                _bitmap?.Recycle();
            }
            catch
            {
            }

            _bitmap = null;
        }

        private void BuildUi()
        {
            int navPad = (int)(56 * Resources.DisplayMetrics.Density + 0.5f);
            var root = new LinearLayout(this) { Orientation = ViewOrientation.Vertical };
            root.SetBackgroundColor(Color.Black);
            root.SetPadding(12, 16, 12, navPad);

            var title = new TextView(this)
            {
                Text = "Adjust document corners",
                TextSize = 14,
            };
            title.SetTextColor(Color.White);
            root.AddView(title, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            _cropView = new DocumentCropView(this, _bitmap);
            root.AddView(_cropView, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));

            var actions = new LinearLayout(this) { Orientation = ViewOrientation.Horizontal };
            actions.SetPadding(0, 12, 0, 0);

            var resetBtn = new AButton(this) { Text = "Reset" };
            resetBtn.Click += (s, e) => _cropView.ResetCorners();
            actions.AddView(resetBtn, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            var cancelBtn = new AButton(this) { Text = "Cancel" };
            cancelBtn.Click += (s, e) =>
            {
                SetResult(Result.Canceled);
                Finish();
            };
            actions.AddView(cancelBtn, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            var applyBtn = new AButton(this) { Text = "Apply" };
            applyBtn.Click += (s, e) => ApplyCrop();
            actions.AddView(applyBtn, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            root.AddView(actions, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            SetContentView(root);
        }

        private void ApplyCrop()
        {
            APointF[] corners = _cropView.GetCornersImageSpace();
            if (corners == null || corners.Length != 4)
            {
                Toast.MakeText(this, "Invalid crop", ToastLength.Short).Show();
                return;
            }

            Bitmap warped = null;
            try
            {
                warped = PerspectiveWarp(_bitmap, corners);
                if (warped == null)
                {
                    Toast.MakeText(this, "Could not crop image", ToastLength.Short).Show();
                    return;
                }

                string tempPath = IOPath.Combine(CacheDir.AbsolutePath, "document_crop_" + Guid.NewGuid().ToString("N") + ".jpg");
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    warped.Compress(Bitmap.CompressFormat.Jpeg, 92, fs);
                }

                var reply = new Intent();
                reply.PutExtra(ExtraResultPath, tempPath);
                SetResult(Result.Ok, reply);
                Finish();
            }
            catch
            {
                Toast.MakeText(this, "Could not crop image", ToastLength.Short).Show();
            }
            finally
            {
                try
                {
                    warped?.Recycle();
                }
                catch
                {
                }
            }
        }

        internal static Bitmap PerspectiveWarp(Bitmap source, APointF[] corners)
        {
            if (source == null || corners == null || corners.Length != 4)
                return null;

            float topWidth = Distance(corners[0], corners[1]);
            float bottomWidth = Distance(corners[3], corners[2]);
            float leftHeight = Distance(corners[0], corners[3]);
            float rightHeight = Distance(corners[1], corners[2]);
            int outW = Math.Max(1, (int)Math.Ceiling(Math.Max(topWidth, bottomWidth)));
            int outH = Math.Max(1, (int)Math.Ceiling(Math.Max(leftHeight, rightHeight)));

            var srcPts = new float[]
            {
                corners[0].X, corners[0].Y,
                corners[1].X, corners[1].Y,
                corners[2].X, corners[2].Y,
                corners[3].X, corners[3].Y,
            };
            var dstPts = new float[]
            {
                0, 0,
                outW, 0,
                outW, outH,
                0, outH,
            };

            var matrix = new Matrix();
            if (!matrix.SetPolyToPoly(srcPts, 0, dstPts, 0, 4))
                return null;

            var output = Bitmap.CreateBitmap(outW, outH, Bitmap.Config.Argb8888);
            var canvas = new Canvas(output);
            canvas.DrawColor(Color.Black);
            var paint = new APaint { FilterBitmap = true, AntiAlias = true };
            canvas.DrawBitmap(source, matrix, paint);
            return output;
        }

        private static float Distance(APointF a, APointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static Intent CreateIntent(Context context, string filePath)
        {
            var intent = new Intent(context, typeof(DocumentCropActivity));
            intent.PutExtra(ExtraPath, filePath);
            return intent;
        }

        private sealed class DocumentCropView : AView
        {
            private readonly Bitmap _bitmap;
            private readonly APointF[] _corners;
            private readonly APaint _bitmapPaint = new APaint { FilterBitmap = true, AntiAlias = true };
            private readonly APaint _linePaint = new APaint { AntiAlias = true, Color = Color.Yellow, StrokeWidth = 3f };
            private readonly APaint _handleFill = new APaint { AntiAlias = true, Color = Color.Yellow };
            private readonly APaint _handleStroke = CreateHandleStrokePaint();

            private float _scale;
            private float _offsetX;
            private float _offsetY;
            private Android.Graphics.RectF _dst = new Android.Graphics.RectF();
            private int _activeCorner = -1;
            private float _hitRadius;

            public DocumentCropView(Context context, Bitmap bitmap) : base(context)
            {
                _bitmap = bitmap;
                _corners = CreateDefaultCorners(bitmap.Width, bitmap.Height);
                _linePaint.SetStyle(APaint.Style.Stroke);
                _hitRadius = 28f * Resources.DisplayMetrics.Density;
            }

            private static APaint CreateHandleStrokePaint()
            {
                var paint = new APaint { AntiAlias = true, Color = Color.Black, StrokeWidth = 2f };
                paint.SetStyle(APaint.Style.Stroke);
                return paint;
            }

            public void ResetCorners()
            {
                APointF[] fresh = CreateDefaultCorners(_bitmap.Width, _bitmap.Height);
                for (int i = 0; i < 4; i++)
                    _corners[i] = fresh[i];
                Invalidate();
            }

            public APointF[] GetCornersImageSpace()
            {
                var copy = new APointF[4];
                for (int i = 0; i < 4; i++)
                    copy[i] = new APointF(_corners[i].X, _corners[i].Y);
                return copy;
            }

            private static APointF[] CreateDefaultCorners(int w, int h)
            {
                float insetX = w * DefaultInsetFraction;
                float insetY = h * DefaultInsetFraction;
                return new[]
                {
                    new APointF(insetX, insetY),
                    new APointF(w - insetX, insetY),
                    new APointF(w - insetX, h - insetY),
                    new APointF(insetX, h - insetY),
                };
            }

            protected override void OnDraw(Canvas canvas)
            {
                base.OnDraw(canvas);
                if (_bitmap == null)
                    return;

                ComputeLayout();
                canvas.DrawBitmap(_bitmap, null, _dst, _bitmapPaint);

                var path = new APath();
                APointF p0 = ImageToView(_corners[0]);
                path.MoveTo(p0.X, p0.Y);
                for (int i = 1; i < 4; i++)
                {
                    APointF p = ImageToView(_corners[i]);
                    path.LineTo(p.X, p.Y);
                }

                path.Close();
                canvas.DrawPath(path, _linePaint);

                float r = _hitRadius * 0.45f;
                for (int i = 0; i < 4; i++)
                {
                    APointF p = ImageToView(_corners[i]);
                    canvas.DrawCircle(p.X, p.Y, r, _handleFill);
                    canvas.DrawCircle(p.X, p.Y, r, _handleStroke);
                }
            }

            private void ComputeLayout()
            {
                int w = Width;
                int h = Height;
                if (w <= 0 || h <= 0 || _bitmap == null)
                    return;

                float pad = 8f * Resources.DisplayMetrics.Density;
                float availW = w - 2 * pad;
                float availH = h - 2 * pad;
                float scale = Math.Min(availW / _bitmap.Width, availH / _bitmap.Height);
                if (scale <= 0)
                    scale = 1f;

                float drawW = _bitmap.Width * scale;
                float drawH = _bitmap.Height * scale;
                _offsetX = (w - drawW) / 2f;
                _offsetY = (h - drawH) / 2f;
                _scale = scale;
                _dst.Set(_offsetX, _offsetY, _offsetX + drawW, _offsetY + drawH);
            }

            private APointF ImageToView(APointF p) =>
                new APointF(p.X * _scale + _offsetX, p.Y * _scale + _offsetY);

            private APointF ViewToImage(float x, float y)
            {
                if (_scale <= 0)
                    return null;
                float ix = (x - _offsetX) / _scale;
                float iy = (y - _offsetY) / _scale;
                ix = Math.Clamp(ix, 0, _bitmap.Width);
                iy = Math.Clamp(iy, 0, _bitmap.Height);
                return new APointF(ix, iy);
            }

            public override bool OnTouchEvent(MotionEvent e)
            {
                float x = e.GetX();
                float y = e.GetY();

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _activeCorner = HitCorner(x, y);
                        return _activeCorner >= 0;
                    case MotionEventActions.Move:
                        if (_activeCorner < 0)
                            return false;
                        APointF img = ViewToImage(x, y);
                        if (img != null)
                        {
                            _corners[_activeCorner] = img;
                            Invalidate();
                        }

                        return true;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _activeCorner = -1;
                        return true;
                }

                return base.OnTouchEvent(e);
            }

            private int HitCorner(float x, float y)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    APointF p = ImageToView(_corners[i]);
                    float dx = p.X - x;
                    float dy = p.Y - y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist <= _hitRadius && dist < bestDist)
                    {
                        bestDist = dist;
                        best = i;
                    }
                }

                return best;
            }
        }
    }
}