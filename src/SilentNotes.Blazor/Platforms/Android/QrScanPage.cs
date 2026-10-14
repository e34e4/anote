// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// A full screen page which shows the camera preview and scans for QR codes. The page
    /// completes the given task source as soon as a code was found, or when the user presses the
    /// close or the back button.
    /// </summary>
    internal class QrScanPage : ContentPage
    {
        private readonly TaskCompletionSource<string> _resultSource;
        private readonly CameraBarcodeReaderView _cameraView;
        private bool _isCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="QrScanPage"/> class.
        /// </summary>
        /// <param name="resultSource">Receives the decoded text, or null when canceled.</param>
        public QrScanPage(TaskCompletionSource<string> resultSource)
        {
            _resultSource = resultSource;
            BackgroundColor = Colors.Black;

            _cameraView = new CameraBarcodeReaderView
            {
                IsDetecting = true,
                Options = new BarcodeReaderOptions
                {
                    Formats = BarcodeFormats.All,
                    AutoRotate = true,
                    TryHarder = true,
                },
            };
            _cameraView.BarcodesDetected += OnBarcodesDetected;

            Button closeButton = CreateOverlayButton("✕");
            closeButton.Clicked += (sender, e) => Complete(null);

            Button torchButton = CreateOverlayButton("🔦");
            torchButton.Clicked += (sender, e) => _cameraView.IsTorchOn = !_cameraView.IsTorchOn;

            var buttonRow = new HorizontalStackLayout
            {
                Spacing = 60,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, 0, 60),
            };
            buttonRow.Children.Add(closeButton);
            buttonRow.Children.Add(torchButton);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            grid.Children.Add(_cameraView);
            grid.Children.Add(buttonRow);
            Content = grid;
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
            _resultSource.TrySetResult(result);
        }
    }
}
