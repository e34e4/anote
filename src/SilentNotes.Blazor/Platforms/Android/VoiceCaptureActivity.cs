// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Speech;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Button = global::Android.Widget.Button;
using Color = global::Android.Graphics.Color;
using Orientation = global::Android.Widget.Orientation;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Standalone Voice capture entry (shortcuts, ASSIST/VOICE_COMMAND, floating menu).
    /// Asks for confirmation, then recognizes speech and appends text (+ optional screenshot) to
    /// the pinned Voice note.
    /// </summary>
    [Register("app.anote.VoiceCaptureActivity")]
    [Activity(
        Name = "app.anote.VoiceCaptureActivity",
        Exported = true,
        Theme = "@android:style/Theme.DeviceDefault.Dialog.NoActionBar",
        ExcludeFromRecents = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    [IntentFilter(
        new[] { Intent.ActionAssist },
        Categories = new[] { Intent.CategoryDefault })]
    [IntentFilter(
        new[] { Intent.ActionVoiceCommand },
        Categories = new[] { Intent.CategoryDefault })]
    [IntentFilter(
        new[] { "android.intent.action.VOICE_ASSIST" },
        Categories = new[] { Intent.CategoryDefault })]
    public class VoiceCaptureActivity : Activity, IRecognitionListener
    {
        private const string LogTag = "aNoteVoiceCapture";
        private const int RequestRecordAudio = 4811;

        private TextView _statusView;
        private Button _yesButton;
        private Button _noButton;
        private SpeechRecognizer _speechRecognizer;
        private string _pendingText;
        private string _pendingScreenshotDataUrl;
        private bool _screenshotReceived;
        private bool _speechFinished;
        private bool _saved;
        private bool _captureStarted;

        /// <inheritdoc/>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var root = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
            };
            root.SetPadding(48, 48, 48, 48);
            root.SetBackgroundColor(Color.Argb(245, 22, 22, 22));

            var title = new TextView(this)
            {
                Text = "aNote Voice",
                TextSize = 20f,
            };
            title.SetTextColor(Color.White);
            root.AddView(title);

            _statusView = new TextView(this)
            {
                Text = "Recognize voice?",
                TextSize = 16f,
            };
            _statusView.SetTextColor(Color.LightGray);
            _statusView.SetPadding(0, 28, 0, 28);
            root.AddView(_statusView);

            var buttons = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
            };

            _noButton = new Button(this) { Text = "No" };
            _noButton.Click += (_, __) => Finish();
            buttons.AddView(_noButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            _yesButton = new Button(this) { Text = "Yes" };
            _yesButton.Click += (_, __) => BeginCaptureAfterConfirm();
            buttons.AddView(_yesButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            root.AddView(buttons);
            SetContentView(root);
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            StopListening();
            base.OnDestroy();
        }

        private void BeginCaptureAfterConfirm()
        {
            if (_captureStarted)
                return;
            _captureStarted = true;
            _yesButton.Enabled = false;
            _noButton.Enabled = false;
            _saved = false;
            _pendingText = null;
            _pendingScreenshotDataUrl = null;
            _screenshotReceived = false;
            _speechFinished = false;

            SetStatus("Preparing…");
            TryCaptureScreenshotViaAccessibility();

            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.RecordAudio) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this, new[] { Manifest.Permission.RecordAudio }, RequestRecordAudio);
                return;
            }

            StartListening();
            ScheduleScreenshotTimeout();
        }

        /// <inheritdoc/>
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode != RequestRecordAudio)
                return;

            if (grantResults != null && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                StartListening();
                ScheduleScreenshotTimeout();
            }
            else
            {
                SetStatus("Microphone permission denied");
                _speechFinished = true;
                TryFinishCapture();
            }
        }

        private void ScheduleScreenshotTimeout()
        {
            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                if (!_screenshotReceived)
                {
                    _screenshotReceived = true;
                    TryFinishCapture();
                }
            }, 2500);
        }

        private void TryCaptureScreenshotViaAccessibility()
        {
            // Best-effort: when the floating accessibility service is connected it can take a
            // screenshot. Assist-session captures use OnHandleScreenshot instead.
            try
            {
                // No direct sync API; leave null unless a future hook fills it.
                _pendingScreenshotDataUrl = null;
            }
            catch
            {
                _pendingScreenshotDataUrl = null;
            }
        }

        private void StartListening()
        {
            try
            {
                if (!SpeechRecognizer.IsRecognitionAvailable(this))
                {
                    SetStatus("Speech recognition unavailable");
                    _speechFinished = true;
                    TryFinishCapture();
                    return;
                }

                SetStatus("Listening… speak now");
                StopListening();
                _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(this);
                _speechRecognizer.SetRecognitionListener(this);

                var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
                intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
                intent.PutExtra(RecognizerIntent.ExtraPrompt, "aNote Voice");
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
            if (_saved || !_speechFinished || !_screenshotReceived)
                return;

            _saved = true;
            bool ok = VoiceCapturePersist.Save(_pendingText, _pendingScreenshotDataUrl);
            SetStatus(ok
                ? "Saved to Voice note"
                : (string.IsNullOrWhiteSpace(_pendingText) ? "Nothing captured" : "Could not save"));

            new Handler(Looper.MainLooper).PostDelayed(Finish, 900);
        }

        private void SetStatus(string text)
        {
            RunOnUiThread(() =>
            {
                if (_statusView != null)
                    _statusView.Text = text ?? string.Empty;
            });
        }

        public void OnReadyForSpeech(Bundle eventParams) => SetStatus("Listening… speak now");
        public void OnBeginningOfSpeech() => SetStatus("Hearing you…");
        public void OnRmsChanged(float rmsdB) { }
        public void OnBufferReceived(byte[] buffer) { }
        public void OnEndOfSpeech() => SetStatus("Recognizing…");

        public void OnError(SpeechRecognizerError error)
        {
            Log.Warn(LogTag, "Speech error: " + error);
            SetStatus("No speech captured");
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
