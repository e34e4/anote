// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using BarcodeScanning;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Full-screen camera page that scans QR codes with native ML Kit via
    /// <c>BarcodeScanning.Native.Maui</c>. A centered square viewfinder helps aiming.
    /// </summary>
    internal class QrScanPage : ContentPage
    {
        private const float ViewfinderFraction = 0.72f;

        private readonly TaskCompletionSource<string> _resultSource;
        private readonly CameraView _cameraView;
        private bool _isCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="QrScanPage"/> class.
        /// </summary>
        /// <param name="resultSource">Receives the decoded text, or null when canceled.</param>
        public QrScanPage(TaskCompletionSource<string> resultSource)
        {
            _resultSource = resultSource;
            BackgroundColor = Colors.Black;

            _cameraView = new CameraView
            {
                CameraEnabled = false,
                PauseScanning = false,
                VibrationOnDetected = false,
                TapToFocusEnabled = true,
                AimMode = false,
                ViewfinderMode = true,
                CaptureQuality = CaptureQuality.High,
                BarcodeSymbologies = BarcodeFormats.QRCode,
                CameraFacing = CameraFacing.Back,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };
            _cameraView.OnDetectionFinished += OnDetectionFinished;

            var overlayView = new GraphicsView
            {
                Drawable = new ViewfinderDrawable(),
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };

            Button closeButton = CreateOverlayButton("✕");
            closeButton.Clicked += (sender, e) => Complete(null);

            Button torchButton = CreateOverlayButton("🔦");
            torchButton.Clicked += (sender, e) => _cameraView.TorchOn = !_cameraView.TorchOn;

            var hint = new Label
            {
                Text = "Align the QR code inside the square",
                TextColor = Colors.White,
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(24, 0, 24, 16),
                Shadow = new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 1),
                    Radius = 2,
                    Opacity = 0.6f,
                },
            };

            var buttonRow = new HorizontalStackLayout
            {
                Spacing = 60,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 0, 48),
            };
            buttonRow.Children.Add(closeButton);
            buttonRow.Children.Add(torchButton);

            var bottomStack = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.End,
                HorizontalOptions = LayoutOptions.Fill,
                Children = { hint, buttonRow },
            };

            var layout = new Grid();
            layout.Children.Add(_cameraView);
            layout.Children.Add(overlayView);
            layout.Children.Add(bottomStack);
            Content = layout;
        }

        /// <inheritdoc/>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _cameraView.CameraEnabled = true;
        }

        /// <inheritdoc/>
        protected override void OnDisappearing()
        {
            _cameraView.CameraEnabled = false;
            _cameraView.PauseScanning = true;
            base.OnDisappearing();
        }

        /// <inheritdoc/>
        protected override bool OnBackButtonPressed()
        {
            Complete(null);
            return true;
        }

        private static Button CreateOverlayButton(string text)
        {
            return new Button
            {
                Text = text,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#99000000"),
                CornerRadius = 28,
                WidthRequest = 56,
                HeightRequest = 56,
                FontSize = 22,
                Padding = new Thickness(0),
            };
        }

        private void OnDetectionFinished(object sender, OnDetectionFinishedEventArg e)
        {
            if (_isCompleted || e?.BarcodeResults == null || e.BarcodeResults.Count == 0)
                return;

            BarcodeResult match = e.BarcodeResults.FirstOrDefault();
            if (match == null)
                return;

            string value = !string.IsNullOrWhiteSpace(match.RawValue) ? match.RawValue : match.DisplayValue;
            if (!string.IsNullOrWhiteSpace(value))
                Complete(value);
        }

        private void Complete(string result)
        {
            if (_isCompleted)
                return;
            _isCompleted = true;

            _cameraView.PauseScanning = true;
            _cameraView.CameraEnabled = false;
            _cameraView.OnDetectionFinished -= OnDetectionFinished;
            _resultSource.TrySetResult(result);
        }

        /// <summary>
        /// Draws a dimmed mask with a clear centered square and corner brackets.
        /// </summary>
        private sealed class ViewfinderDrawable : IDrawable
        {
            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                float width = dirtyRect.Width;
                float height = dirtyRect.Height;
                if (width <= 0 || height <= 0)
                    return;

                float side = Math.Min(width, height) * ViewfinderFraction;
                float left = (width - side) / 2f;
                float top = (height - side) / 2f;
                var hole = new RectF(left, top, side, side);

                canvas.FillColor = Color.FromRgba(0, 0, 0, 140);
                canvas.FillRectangle(0, 0, width, top);
                canvas.FillRectangle(0, top + side, width, height - top - side);
                canvas.FillRectangle(0, top, left, side);
                canvas.FillRectangle(left + side, top, width - left - side, side);

                float corner = Math.Max(18f, side * 0.12f);
                float stroke = Math.Max(3f, side * 0.012f);
                canvas.StrokeColor = Color.FromRgb(76, 175, 80);
                canvas.StrokeSize = stroke;
                canvas.StrokeLineCap = LineCap.Square;

                // Top-left
                canvas.DrawLine(hole.Left, hole.Top, hole.Left + corner, hole.Top);
                canvas.DrawLine(hole.Left, hole.Top, hole.Left, hole.Top + corner);
                // Top-right
                canvas.DrawLine(hole.Right - corner, hole.Top, hole.Right, hole.Top);
                canvas.DrawLine(hole.Right, hole.Top, hole.Right, hole.Top + corner);
                // Bottom-left
                canvas.DrawLine(hole.Left, hole.Bottom, hole.Left + corner, hole.Bottom);
                canvas.DrawLine(hole.Left, hole.Bottom - corner, hole.Left, hole.Bottom);
                // Bottom-right
                canvas.DrawLine(hole.Right - corner, hole.Bottom, hole.Right, hole.Bottom);
                canvas.DrawLine(hole.Right, hole.Bottom - corner, hole.Right, hole.Bottom);
            }
        }
    }
}
