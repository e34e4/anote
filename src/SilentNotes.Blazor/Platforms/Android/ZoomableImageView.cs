// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace SilentNotes
{
    /// <summary>
    /// <see cref="ImageView"/> with pinch-to-zoom and pan when zoomed past fit-center scale.
    /// </summary>
    internal sealed class ZoomableImageView : ImageView, ScaleGestureDetector.IOnScaleGestureListener
    {
        private const float MaxZoomOverFit = 5f;
        private const float DefaultZoomEpsilon = 1.02f;

        private readonly Matrix _matrix = new Matrix();
        private readonly Matrix _fitMatrix = new Matrix();
        private readonly float[] _matrixValues = new float[9];
        private readonly ScaleGestureDetector _scaleDetector;

        private float _lastTouchX;
        private float _lastTouchY;
        private bool _isPanning;

        public ZoomableImageView(Context context)
            : base(context)
        {
            _scaleDetector = new ScaleGestureDetector(context, this);
            SetScaleType(ScaleType.Matrix);
        }

        /// <summary>True when gallery swipe should be allowed (scale ≈ 1).</summary>
        public bool IsAtDefaultZoom
        {
            get
            {
                float fit = GetFitScale();
                if (fit <= 0f)
                    return true;
                return GetCurrentScale() <= fit * DefaultZoomEpsilon;
            }
        }

        /// <summary>Resets zoom/pan after a new image is shown.</summary>
        public void ResetTransform()
        {
            _isPanning = false;
            ComputeFitMatrix();
            _matrix.Set(_fitMatrix);
            ImageMatrix = _matrix;
        }

        /// <summary>Routes touch events for pinch zoom and pan.</summary>
        public bool HandleTouch(MotionEvent e)
        {
            if (e == null)
                return false;

            bool scaling = _scaleDetector.OnTouchEvent(e);

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    _lastTouchX = e.GetX();
                    _lastTouchY = e.GetY();
                    _isPanning = false;
                    break;
                case MotionEventActions.Move:
                    if (!IsAtDefaultZoom && e.PointerCount == 1 && !_scaleDetector.IsInProgress)
                    {
                        float x = e.GetX();
                        float y = e.GetY();
                        _matrix.PostTranslate(x - _lastTouchX, y - _lastTouchY);
                        _lastTouchX = x;
                        _lastTouchY = y;
                        _isPanning = true;
                        ConstrainTranslation();
                        ImageMatrix = _matrix;
                    }
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    _isPanning = false;
                    if (IsAtDefaultZoom)
                        ResetTransform();
                    break;
            }

            return scaling || _isPanning || e.PointerCount > 1;
        }

        /// <inheritdoc/>
        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            ResetTransform();
        }

        /// <inheritdoc/>
        public bool OnScale(ScaleGestureDetector detector)
        {
            if (Drawable == null || detector == null)
                return false;

            float scaleFactor = detector.ScaleFactor;
            if (float.IsNaN(scaleFactor) || float.IsInfinity(scaleFactor) || scaleFactor == 0f)
                return false;

            _matrix.PostScale(scaleFactor, scaleFactor, detector.FocusX, detector.FocusY);
            ClampScale(detector.FocusX, detector.FocusY);
            ConstrainTranslation();
            ImageMatrix = _matrix;
            return true;
        }

        /// <inheritdoc/>
        public bool OnScaleBegin(ScaleGestureDetector detector) => Drawable != null;

        /// <inheritdoc/>
        public void OnScaleEnd(ScaleGestureDetector detector)
        {
            if (IsAtDefaultZoom)
                ResetTransform();
        }

        private void ComputeFitMatrix()
        {
            _fitMatrix.Reset();
            var d = Drawable;
            if (d == null)
                return;

            int vw = Width - PaddingLeft - PaddingRight;
            int vh = Height - PaddingTop - PaddingBottom;
            if (vw <= 0 || vh <= 0)
                return;

            int dw = d.IntrinsicWidth;
            int dh = d.IntrinsicHeight;
            if (dw <= 0 || dh <= 0)
                return;

            float scale = Math.Min((float)vw / dw, (float)vh / dh);
            float dx = PaddingLeft + (vw - dw * scale) * 0.5f;
            float dy = PaddingTop + (vh - dh * scale) * 0.5f;
            _fitMatrix.SetScale(scale, scale);
            _fitMatrix.PostTranslate(dx, dy);
        }

        private float GetFitScale()
        {
            _fitMatrix.GetValues(_matrixValues);
            return _matrixValues[Matrix.MscaleX];
        }

        private float GetCurrentScale()
        {
            _matrix.GetValues(_matrixValues);
            return _matrixValues[Matrix.MscaleX];
        }

        private void ClampScale(float focusX, float focusY)
        {
            float fit = GetFitScale();
            if (fit <= 0f)
                return;

            float current = GetCurrentScale();
            float max = fit * MaxZoomOverFit;
            if (current < fit)
            {
                float fix = fit / current;
                _matrix.PostScale(fix, fix, focusX, focusY);
            }
            else if (current > max)
            {
                float fix = max / current;
                _matrix.PostScale(fix, fix, focusX, focusY);
            }
        }

        private void ConstrainTranslation()
        {
            if (Drawable == null)
                return;

            _matrix.GetValues(_matrixValues);
            float transX = _matrixValues[Matrix.MtransX];
            float transY = _matrixValues[Matrix.MtransY];
            float scaleX = _matrixValues[Matrix.MscaleX];
            float scaleY = _matrixValues[Matrix.MscaleY];

            int dw = Drawable.IntrinsicWidth;
            int dh = Drawable.IntrinsicHeight;
            float scaledW = dw * scaleX;
            float scaledH = dh * scaleY;

            float minX = Math.Min(0, Width - scaledW);
            float maxX = Math.Max(0, Width - scaledW);
            float minY = Math.Min(0, Height - scaledH);
            float maxY = Math.Max(0, Height - scaledH);

            float clampedX = Math.Max(minX, Math.Min(transX, maxX));
            float clampedY = Math.Max(minY, Math.Min(transY, maxY));
            if (Math.Abs(clampedX - transX) > 0.5f || Math.Abs(clampedY - transY) > 0.5f)
                _matrix.PostTranslate(clampedX - transX, clampedY - transY);
        }
    }
}