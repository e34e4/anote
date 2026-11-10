// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using AWebView = Android.Webkit.WebView;
using File = System.IO.File;
using IOPath = System.IO.Path;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Writes the current WebView contents to a PDF file by drawing pages into <see cref="PdfDocument"/>.
    /// Avoids PrintDocumentAdapter callbacks (no usable public ctor in current .NET Android bindings).
    /// </summary>
    internal static class AbroWebViewPdfExporter
    {
        private const int PageWidth = 595;  // A4 @ 72dpi
        private const int PageHeight = 842;

        public static Task<bool> TryExportAsync(AWebView webView, string outputPath)
        {
            if (webView == null || string.IsNullOrWhiteSpace(outputPath))
                return Task.FromResult(false);

            var tcs = new TaskCompletionSource<bool>();
            try
            {
                // WebView drawing must run on the UI thread.
                new Handler(Looper.MainLooper).Post(() =>
                {
                    try
                    {
                        tcs.TrySetResult(ExportOnUiThread(webView, outputPath));
                    }
                    catch
                    {
                        tcs.TrySetResult(false);
                    }
                });
            }
            catch
            {
                tcs.TrySetResult(false);
            }

            return tcs.Task;
        }

        private static bool ExportOnUiThread(AWebView webView, string outputPath)
        {
            string directory = IOPath.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            int contentWidth = Math.Max(webView.Width, 1);
            // ContentHeight is CSS px; ScaleX tracks the rendered zoom without the deprecated Scale API.
            float pageScale = webView.ScaleX > 0.01f ? webView.ScaleX : 1f;
            int contentHeight = Math.Max(
                (int)Math.Ceiling(webView.ContentHeight * pageScale),
                Math.Max(webView.Height, 1));

            // Cap extreme pages to keep memory reasonable (~8k CSS px).
            contentHeight = Math.Min(contentHeight, 8000);

            Bitmap bitmap = null;
            PdfDocument document = null;
            string temp = outputPath + ".tmp";
            try
            {
                bitmap = Bitmap.CreateBitmap(contentWidth, contentHeight, Bitmap.Config.Argb8888);
                var canvas = new Canvas(bitmap);
                canvas.DrawColor(Android.Graphics.Color.White);
                webView.Draw(canvas);

                document = new PdfDocument();
                float scale = (float)PageWidth / contentWidth;
                int scaledHeight = Math.Max(1, (int)Math.Ceiling(contentHeight * scale));
                int pageCount = Math.Max(1, (int)Math.Ceiling(scaledHeight / (double)PageHeight));

                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    PdfDocument.PageInfo pageInfo = new PdfDocument.PageInfo.Builder(
                        PageWidth, PageHeight, pageIndex + 1).Create();
                    PdfDocument.Page page = document.StartPage(pageInfo);
                    Canvas pdfCanvas = page.Canvas;
                    pdfCanvas.Save();
                    pdfCanvas.Scale(scale, scale);
                    float srcTop = pageIndex * PageHeight / scale;
                    pdfCanvas.Translate(0, -srcTop);
                    pdfCanvas.DrawBitmap(bitmap, 0, 0, null);
                    pdfCanvas.Restore();
                    document.FinishPage(page);
                }

                if (File.Exists(temp))
                    File.Delete(temp);
                using (var stream = File.Open(temp, FileMode.Create, FileAccess.Write))
                {
                    document.WriteTo(stream);
                }

                if (!File.Exists(temp) || new FileInfo(temp).Length <= 0)
                {
                    TryDelete(temp);
                    return false;
                }

                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                File.Move(temp, outputPath);
                temp = null; // moved; skip cleanup delete
                return true;
            }
            finally
            {
                try { document?.Close(); } catch { }
                try { bitmap?.Recycle(); } catch { }
                if (temp != null)
                    TryDelete(temp);
            }
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
