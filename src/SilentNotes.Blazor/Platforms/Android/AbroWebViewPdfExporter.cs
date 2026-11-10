// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.OS;
using Android.Print;
using AWebView = Android.Webkit.WebView;
using Java.IO;
using File = System.IO.File;
using IOException = System.IO.IOException;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Writes the current WebView contents to a PDF file via the print adapter (no system dialog).
    /// </summary>
    internal static class AbroWebViewPdfExporter
    {
        public static Task<bool> TryExportAsync(AWebView webView, string outputPath)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (webView == null || string.IsNullOrWhiteSpace(outputPath))
            {
                tcs.TrySetResult(false);
                return tcs.Task;
            }

            try
            {
                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string jobName = Path.GetFileNameWithoutExtension(outputPath) ?? "abro-page";
                PrintDocumentAdapter adapter = webView.CreatePrintDocumentAdapter(jobName);
                var attributes = new PrintAttributes.Builder()
                    .SetMediaSize(PrintAttributes.MediaSize.IsoA4)
                    .SetResolution(new PrintAttributes.Resolution("pdf", "pdf", 600, 600))
                    .SetMinMargins(PrintAttributes.Margins.NoMargins)
                    .Build();

                adapter.OnLayout(
                    null,
                    attributes,
                    null,
                    new LayoutCallback(adapter, outputPath, tcs),
                    new Bundle());
            }
            catch (Exception)
            {
                tcs.TrySetResult(false);
            }

            return tcs.Task;
        }

        private sealed class LayoutCallback : PrintDocumentAdapter.LayoutResultCallback
        {
            private readonly PrintDocumentAdapter _adapter;
            private readonly string _outputPath;
            private readonly TaskCompletionSource<bool> _tcs;

            public LayoutCallback(PrintDocumentAdapter adapter, string outputPath, TaskCompletionSource<bool> tcs)
            {
                _adapter = adapter;
                _outputPath = outputPath;
                _tcs = tcs;
            }

            public override void OnLayoutFinished(PrintDocumentInfo info, bool changed)
            {
                try
                {
                    string temp = _outputPath + ".tmp";
                    if (File.Exists(temp))
                        File.Delete(temp);

                    var javaFile = new Java.IO.File(temp);
                    ParcelFileDescriptor descriptor = ParcelFileDescriptor.Open(
                        javaFile,
                        ParcelFileMode.ReadWrite | ParcelFileMode.Create | ParcelFileMode.Truncate);

                    var pages = new PageRange[] { PageRange.AllPages };
                    _adapter.OnWrite(
                        pages,
                        descriptor,
                        new CancellationSignal(),
                        new WriteCallback(descriptor, temp, _outputPath, _tcs));
                }
                catch (Exception)
                {
                    _tcs.TrySetResult(false);
                }
            }

            public override void OnLayoutCancelled()
            {
                _tcs.TrySetResult(false);
            }

            public override void OnLayoutFailed(Java.Lang.ICharSequence error)
            {
                _tcs.TrySetResult(false);
            }
        }

        private sealed class WriteCallback : PrintDocumentAdapter.WriteResultCallback
        {
            private readonly ParcelFileDescriptor _descriptor;
            private readonly string _tempPath;
            private readonly string _outputPath;
            private readonly TaskCompletionSource<bool> _tcs;

            public WriteCallback(
                ParcelFileDescriptor descriptor,
                string tempPath,
                string outputPath,
                TaskCompletionSource<bool> tcs)
            {
                _descriptor = descriptor;
                _tempPath = tempPath;
                _outputPath = outputPath;
                _tcs = tcs;
            }

            public override void OnWriteFinished(PageRange[] pages)
            {
                try
                {
                    _descriptor?.Close();
                    if (!File.Exists(_tempPath) || new System.IO.FileInfo(_tempPath).Length <= 0)
                    {
                        TryDelete(_tempPath);
                        _tcs.TrySetResult(false);
                        return;
                    }

                    if (File.Exists(_outputPath))
                        File.Delete(_outputPath);
                    File.Move(_tempPath, _outputPath);
                    _tcs.TrySetResult(true);
                }
                catch (Exception)
                {
                    TryDelete(_tempPath);
                    _tcs.TrySetResult(false);
                }
            }

            public override void OnWriteCancelled()
            {
                try { _descriptor?.Close(); } catch (IOException) { }
                TryDelete(_tempPath);
                _tcs.TrySetResult(false);
            }

            public override void OnWriteFailed(Java.Lang.ICharSequence error)
            {
                try { _descriptor?.Close(); } catch (IOException) { }
                TryDelete(_tempPath);
                _tcs.TrySetResult(false);
            }

            private static void TryDelete(string path)
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                }
            }
        }
    }
}
