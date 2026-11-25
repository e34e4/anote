// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.IO;
using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using SilentNotes.Platforms;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android launcher for <see cref="RecVoiceMemoActivity"/>.
    /// </summary>
    internal class RecVoiceMemoLauncher : IRecVoiceMemoLauncher
    {
        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public bool TryLaunchTranscribe(string recordingFilePath, string recordingFileName)
        {
            if (string.IsNullOrWhiteSpace(recordingFilePath) || !File.Exists(recordingFilePath))
                return false;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Context context = global::Android.App.Application.Context;
                if (context == null)
                    return;

                var intent = new Intent(context, typeof(RecVoiceMemoActivity));
                intent.PutExtra(RecVoiceMemoActivity.ExtraRecordingPath, recordingFilePath);
                intent.PutExtra(RecVoiceMemoActivity.ExtraRecordingName, recordingFileName ?? Path.GetFileName(recordingFilePath));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                context.StartActivity(intent);
            });

            return true;
        }
    }
}