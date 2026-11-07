// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Android.Content;
using Android.OS;
using Android.Speech;
using Android.Util;
using Android.Views;
using Android.Widget;
using Android.Service.Voice;
using Bitmap = global::Android.Graphics.Bitmap;
using Button = Android.Widget.Button;
using Color = global::Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Assist-gesture session: asks "Recognize voice?", then listens, receives a screenshot when
    /// available, and prepends both to the pinned Voice note.
    /// </summary>
    internal sealed class AnoteVoiceInteractionSession : VoiceInteractionSession, IRecognitionListener
    {
        private const string LogTag = "aNoteAssist";

        private readonly Context _context;
        private TextView _statusView;
        private LinearLayout _confirmRow;
        private Button _yesButton;
        private Button _noButton;
        private SpeechRecognizer _speechRecognizer;
        private string _pendingScreenshotDataUrl;
        private string _pendingText;
        private bool _screenshotReceived;
        private bool _speechFinished;
        private bool _saved;
        private bool _captureStarted;
        private bool _userDeclined;

        public AnoteVoiceInteractionSession(Context context)
            : base(context)
        {
            _context = context;
        }

        /// <inheritdoc/>
        public override global::Android.Views.View OnCreateContentView()
        {
            var root = new LinearLayout(_context)
            {
                Orientation = Orientation.Vertical,
            };
            root.SetPadding(48, 48, 48, 48);
            root.SetBackgroundColor(Color.Argb(230, 18, 18, 18));

            var title = new TextView(_context)
            {
                Text = "aNote Voice",
                TextSize = 20f,
            };
            title.SetTextColor(Color.White);
            root.AddView(title);

            _statusView = new TextView(_context)
            {
                Text = "Recognize voice?",
                TextSize = 16f,
            };
            _statusView.SetTextColor(Color.LightGray);
            _statusView.SetPadding(0, 24, 0, 24);
            root.AddView(_statusView);

            _confirmRow = new LinearLayout(_context)
            {
                Orientation = Orientation.Horizontal,
            };

            _noButton = new Button(_context) { Text = "No" };
            _noButton.Click += (_, __) =>
            {
                _userDeclined = true;
                Hide();
            };
            _confirmRow.AddView(_noButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            _yesButton = new Button(_context) { Text = "Yes" };
            _yesButton.Click += (_, __) => BeginCaptureAfterConfirm();
            _confirmRow.AddView(_yesButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            root.AddView(_confirmRow);
            return root;
        }

        /// <inheritdoc/>
        public override void OnShow(Bundle args, ShowFlags showFlags)
        {
            base.OnShow(args, showFlags);
            _saved = false;
            _captureStarted = false;
            _userDeclined = false;
            _pendingText = null;
            _pendingScreenshotDataUrl = null;
            _screenshotReceived = false;
            _speechFinished = false;
            SetStatus("Recognize voice?");
            ShowConfirmButtons(true);

            // Keep collecting the assist screenshot even before the user confirms, so Yes can
            // save it together with the transcript.
            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                if (!_screenshotReceived)
                    _screenshotReceived = true;
            }, 1800);
        }

        /// <inheritdoc/>
        public override void OnHandleScreenshot(Bitmap screenshot)
        {
            try
            {
                if (screenshot != null && !screenshot.IsRecycled)
                    _pendingScreenshotDataUrl = VoiceCapturePersist.BitmapToJpegDataUrl(screenshot);
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "Screenshot encode failed: " + ex.Message);
            }
            finally
            {
                _screenshotReceived = true;
                TryFinishCapture();
            }
        }

        /// <inheritdoc/>
        public override void OnHide()
        {
            StopListening();
            base.OnHide();
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            StopListening();
            base.OnDestroy();
        }

        private void BeginCaptureAfterConfirm()
        {
            if (_captureStarted || _userDeclined)
                return;

            _captureStarted = true;
            ShowConfirmButtons(false);
            SetStatus("Listening… speak now");
            StartListening();

            if (!_screenshotReceived)
            {
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (!_screenshotReceived)
                    {
                        _screenshotReceived = true;
                        TryFinishCapture();
                    }
                }, 1800);
            }
        }

        private void ShowConfirmButtons(bool visible)
        {
            if (_confirmRow == null)
                return;
            _confirmRow.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
            if (_yesButton != null)
                _yesButton.Enabled = visible;
            if (_noButton != null)
                _noButton.Enabled = visible;
        }

        private void StartListening()
        {
            try
            {
                if (!SpeechRecognizer.IsRecognitionAvailable(_context))
                {
                    SetStatus("Speech recognition unavailable — saving screenshot only");
                    _speechFinished = true;
                    TryFinishCapture();
                    return;
                }

                StopListening();
                _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(_context);
                _speechRecognizer.SetRecognitionListener(this);

                var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
                intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
                _speechRecognizer.StartListening(intent);
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "StartListening failed: " + ex.Message);
                SetStatus("Could not start listening");
                _speechFinished = true;
                TryFinishCapture();
            }
        }

        private void StopListening()
        {
            try
            {
                _speechRecognizer?.Cancel();
                _speechRecognizer?.Destroy();
            }
            catch
            {
                // ignored
            }
            finally
            {
                _speechRecognizer = null;
            }
        }

        private void TryFinishCapture()
        {
            if (!_captureStarted || _userDeclined || _saved || !_speechFinished || !_screenshotReceived)
                return;

            _saved = true;
            bool ok = VoiceCapturePersist.Save(_pendingText, _pendingScreenshotDataUrl);
            SetStatus(ok
                ? "Saved to Voice note"
                : (string.IsNullOrWhiteSpace(_pendingText) && string.IsNullOrEmpty(_pendingScreenshotDataUrl)
                    ? "Nothing captured"
                    : "Could not save"));

            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                try { Hide(); } catch { /* ignored */ }
            }, 900);
        }

        private void SetStatus(string text)
        {
            if (_statusView == null)
                return;
            _statusView.Post(() => _statusView.Text = text ?? string.Empty);
        }

        public void OnReadyForSpeech(Bundle eventParams) => SetStatus("Listening… speak now");
        public void OnBeginningOfSpeech() => SetStatus("Hearing you…");
        public void OnRmsChanged(float rmsdB) { }
        public void OnBufferReceived(byte[] buffer) { }
        public void OnEndOfSpeech() => SetStatus("Recognizing…");

        public void OnError(SpeechRecognizerError error)
        {
            Log.Warn(LogTag, "Speech error: " + error);
            SetStatus("No speech — saving screenshot if available");
            _speechFinished = true;
            TryFinishCapture();
        }

        public void OnResults(Bundle results)
        {
            _pendingText = FirstResult(results);
            SetStatus(string.IsNullOrWhiteSpace(_pendingText) ? "No text recognized" : "Got it");
            _speechFinished = true;
            TryFinishCapture();
        }

        public void OnPartialResults(Bundle partialResults)
        {
            string partial = FirstResult(partialResults);
            if (!string.IsNullOrWhiteSpace(partial))
                SetStatus(partial);
        }

        public void OnEvent(int eventType, Bundle eventParams) { }

        private static string FirstResult(Bundle results)
        {
            IList<string> list = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (list == null || list.Count == 0)
                return null;
            return list[0];
        }
    }
}
