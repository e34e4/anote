// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Fallback QR camera page using ZXing, with the same centered square viewfinder as the
    /// ML Kit scanner. Used when native ML Kit barcode clients cannot be created.
    /// </summary>
    internal class QrScanPageZxing : ContentPage
    {
        private const float ViewfinderFraction = 0.72f;

        private readonly TaskCompletionSource<string> _resultSource;
        private readonly CameraBarcodeReaderView _cameraView;
        private bool _isCompleted;

        public QrScanPageZxing(TaskCompletionSource<string> resultSource)
        {
            _resultSource = resultSource;
            BackgroundColor = Colors.Black;

            _cameraView = new CameraBarcodeReaderView
            {
                IsDetecting = true,
                Options = new BarcodeReaderOptions
                {
                    Formats = BarcodeFormats.QrCode,
                    AutoRotate = true,
                    TryHarder = true,
                },
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };
            _cameraView.BarcodesDetected += OnBarcodesDetected;

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
            torchButton.Clicked += (sender, e) => _cameraView.IsTorchOn = !_cameraView.IsTorchOn;

            var hint = new Label
            {
                Text = "Align the QR code inside the square",
                TextColor = Colors.White,
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(24, 0, 24, 16),
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

        private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
        {
            if ((e.Results != null) && (e.Results.Length > 0))
                Complete(e.Results[0].Value);
        }

        private void Complete(string result)
        {
            if (_isCompleted)
                return;
            _isCompleted = true;

            _cameraView.IsDetecting = false;
            _cameraView.BarcodesDetected -= OnBarcodesDetected;
            _resultSource.TrySetResult(result);
        }

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

                canvas.DrawLine(hole.Left, hole.Top, hole.Left + corner, hole.Top);
                canvas.DrawLine(hole.Left, hole.Top, hole.Left, hole.Top + corner);
                canvas.DrawLine(hole.Right - corner, hole.Top, hole.Right, hole.Top);
                canvas.DrawLine(hole.Right, hole.Top, hole.Right, hole.Top + corner);
                canvas.DrawLine(hole.Left, hole.Bottom, hole.Left + corner, hole.Bottom);
                canvas.DrawLine(hole.Left, hole.Bottom - corner, hole.Left, hole.Bottom);
                canvas.DrawLine(hole.Right - corner, hole.Bottom, hole.Right, hole.Bottom);
                canvas.DrawLine(hole.Right, hole.Bottom - corner, hole.Right, hole.Bottom);
            }
        }
    }
}
