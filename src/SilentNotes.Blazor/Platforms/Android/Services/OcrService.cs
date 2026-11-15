// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.Gms.Tasks;
using Android.Graphics;
using SilentNotes.Services;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using MlKitText = Xamarin.Google.MLKit.Vision.Text.Text;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IOcrService"/> using the bundled ML Kit text
    /// recognition model.
    /// </summary>
    internal class OcrService : IOcrService
    {
        private readonly IAppContextService _appContext;

        public OcrService(IAppContextService appContextService)
        {
            _appContext = appContextService;
        }

        /// <inheritdoc/>
        public bool IsSupported
        {
            get { return _appContext?.Context != null; }
        }

        /// <inheritdoc/>
        public async Task<string> RecognizeTextAsync(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return null;

            MainApplication.EnsureMlKitInitialized(_appContext?.Context);

            Bitmap bitmap = null;
            ITextRecognizer recognizer = null;
            try
            {
                bitmap = BitmapFactory.DecodeByteArray(imageBytes, 0, imageBytes.Length);
                if (bitmap == null)
                    return null;

                recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
                InputImage image = InputImage.FromBitmap(bitmap, 0);
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                recognizer.Process(image)
                    .AddOnSuccessListener(new SuccessListener(completion))
                    .AddOnFailureListener(new FailureListener(completion));

                string result = await completion.Task;
                return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                try
                {
                    recognizer?.Close();
                }
                catch (Exception)
                {
                }

                try
                {
                    if (bitmap != null && !bitmap.IsRecycled)
                        bitmap.Recycle();
                }
                catch (Exception)
                {
                }
            }
        }

        private sealed class SuccessListener : Java.Lang.Object, IOnSuccessListener
        {
            private readonly TaskCompletionSource<string> _completion;

            public SuccessListener(TaskCompletionSource<string> completion)
            {
                _completion = completion;
            }

            public void OnSuccess(Java.Lang.Object result)
            {
                string text = (result as MlKitText)?.GetText();
                _completion.TrySetResult(text);
            }
        }

        private sealed class FailureListener : Java.Lang.Object, IOnFailureListener
        {
            private readonly TaskCompletionSource<string> _completion;

            public FailureListener(TaskCompletionSource<string> completion)
            {
                _completion = completion;
            }

            public void OnFailure(Java.Lang.Exception e)
            {
                _completion.TrySetResult(null);
            }
        }
    }
}
