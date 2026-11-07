// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Speech;
using Android.Util;
using Android.Views;
using Android.Widget;
using Android.Service.Voice;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using Orientation = Android.Widget.Orientation;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Assist-gesture session: listens for voice, receives a screenshot when available, and
    /// prepends both to the pinned Voice note.
    /// </summary>
    internal sealed class AnoteVoiceInteractionSession : VoiceInteractionSession, IRecognitionListener
    {
        private const string LogTag = "aNoteAssist";
        private const int MaxScreenshotEdge = 1200;
        private const int JpegQuality = 72;

        private readonly Context _context;
        private TextView _statusView;
        private SpeechRecognizer _speechRecognizer;
        private string _pendingScreenshotDataUrl;
        private string _pendingText;
        private bool _screenshotReceived;
        private bool _speechFinished;
        private bool _saved;

        public AnoteVoiceInteractionSession(Context context)
            : base(context)
        {
            _context = context;
        }

        /// <inheritdoc/>
        public override View OnCreateContentView()
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
                Text = "Listening… speak now",
                TextSize = 16f,
            };
            _statusView.SetTextColor(Color.LightGray);
            _statusView.SetPadding(0, 24, 0, 0);
            root.AddView(_statusView);

            return root;
        }

        /// <inheritdoc/>
        public override void OnShow(Bundle args, ShowFlags showFlags)
        {
            base.OnShow(args, showFlags);
            _saved = false;
            _pendingText = null;
            _pendingScreenshotDataUrl = null;
            _screenshotReceived = false;
            _speechFinished = false;
            SetStatus("Listening… speak now");
            StartListening();

            // Assist may omit screenshots (user/policy). Don't block the capture forever.
            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                if (!_screenshotReceived)
                {
                    _screenshotReceived = true;
                    TryFinishCapture();
                }
            }, 1800);
        }

        /// <inheritdoc/>
        public override void OnHandleScreenshot(Bitmap screenshot)
        {
            try
            {
                if (screenshot != null && !screenshot.IsRecycled)
                    _pendingScreenshotDataUrl = BitmapToJpegDataUrl(screenshot);
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
            if (_saved || !_speechFinished || !_screenshotReceived)
                return;

            _saved = true;
            PersistCapture(_pendingText, _pendingScreenshotDataUrl);
            SetStatus(string.IsNullOrWhiteSpace(_pendingText) && string.IsNullOrEmpty(_pendingScreenshotDataUrl)
                ? "Nothing captured"
                : "Saved to Voice note");

            // Close the assist UI shortly after saving.
            new Handler(Looper.MainLooper).PostDelayed(() =>
            {
                try { Hide(); } catch { /* ignored */ }
            }, 900);
        }

        private void PersistCapture(string text, string screenshotDataUrl)
        {
            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrEmpty(screenshotDataUrl))
                return;

            try
            {
                IRepositoryStorageService repo = Ioc.Instance.GetService<IRepositoryStorageService>();
                if (repo == null)
                {
                    Log.Warn(LogTag, "Repository service unavailable");
                    return;
                }

                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                NoteModel voiceNote = VoiceNoteHelper.EnsureVoiceNote(model);
                VoiceNoteHelper.PrependCapture(voiceNote, text, screenshotDataUrl, DateTime.UtcNow);
                repo.TrySaveRepository(model);

                try
                {
                    IMessengerService messenger = Ioc.Instance.GetService<IMessengerService>();
                    messenger?.Send(new VoiceNoteChangedMessage());
                }
                catch
                {
                    // Messenger may be unavailable outside the Blazor UI.
                }
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "PersistCapture failed: " + ex);
            }
        }

        private void SetStatus(string text)
        {
            if (_statusView == null)
                return;
            _statusView.Post(() => _statusView.Text = text ?? string.Empty);
        }

        private static string BitmapToJpegDataUrl(Bitmap source)
        {
            Bitmap scaled = source;
            bool disposeScaled = false;
            try
            {
                int maxEdge = Math.Max(source.Width, source.Height);
                if (maxEdge > MaxScreenshotEdge)
                {
                    float scale = MaxScreenshotEdge / (float)maxEdge;
                    int w = Math.Max(1, (int)(source.Width * scale));
                    int h = Math.Max(1, (int)(source.Height * scale));
                    scaled = Bitmap.CreateScaledBitmap(source, w, h, true);
                    disposeScaled = !ReferenceEquals(scaled, source);
                }

                using var stream = new MemoryStream();
                if (!scaled.Compress(Bitmap.CompressFormat.Jpeg, JpegQuality, stream))
                    return null;

                string b64 = Convert.ToBase64String(stream.ToArray());
                return "data:image/jpeg;base64," + b64;
            }
            finally
            {
                if (disposeScaled && scaled != null && !scaled.IsRecycled)
                    scaled.Recycle();
            }
        }

        // --- IRecognitionListener ---

        public void OnReadyForSpeech(Bundle eventParams)
        {
            SetStatus("Listening… speak now");
        }

        public void OnBeginningOfSpeech()
        {
            SetStatus("Hearing you…");
        }

        public void OnRmsChanged(float rmsdB)
        {
        }

        public void OnBufferReceived(byte[] buffer)
        {
        }

        public void OnEndOfSpeech()
        {
            SetStatus("Recognizing…");
        }

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

        public void OnEvent(int eventType, Bundle eventParams)
        {
        }

        private static string FirstResult(Bundle results)
        {
            IList<string> list = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (list == null || list.Count == 0)
                return null;
            return list[0];
        }
    }
}
