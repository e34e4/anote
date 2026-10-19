// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics.Pdf;
using Android.OS;
using Android.Provider;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using AndroidEnv = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;
using Bitmap = Android.Graphics.Bitmap;
using Color = Android.Graphics.Color;
using FileProvider = AndroidX.Core.Content.FileProvider;
using Path = System.IO.Path;
using Settings = Android.Provider.Settings;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation of <see cref="IDocumentReaderService"/>: browse storage and open
    /// text / Office / PDF / EPUB / FB2 documents read-only (DjVu via external viewer).
    /// </summary>
    internal class DocumentReaderService : IDocumentReaderService, IDisposable
    {
        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;

        private string _currentDirectory;
        private DocumentContentModel _activeDocument;
        private PdfRenderer _pdfRenderer;
        private ParcelFileDescriptor _pdfFd;

        public DocumentReaderService(IAppContextService appContext, IActivityResultAwaiter activityResultAwaiter)
        {
            _appContext = appContext;
            _activityResultAwaiter = activityResultAwaiter;
            _currentDirectory = GetDefaultRootPath();
        }

        /// <inheritdoc/>
        public bool IsSupported => true;

        /// <inheritdoc/>
        public bool HasAllFilesPermission
        {
            get
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                    return AndroidEnv.IsExternalStorageManager;
                return true;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestAllFilesPermissionAsync()
        {
            if (HasAllFilesPermission)
                return true;

            if (Build.VERSION.SdkInt < BuildVersionCodes.R)
                return true;

            try
            {
                Activity activity = _appContext.RootActivity;
                Intent intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
                intent.SetData(AndroidUri.Parse("package:" + activity.PackageName));
                await _activityResultAwaiter.StartActivityAndWaitForResult(activity, intent);
            }
            catch (Exception)
            {
                try
                {
                    Activity activity = _appContext.RootActivity;
                    Intent intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
                    await _activityResultAwaiter.StartActivityAndWaitForResult(activity, intent);
                }
                catch (Exception)
                {
                }
            }

            return HasAllFilesPermission;
        }

        /// <inheritdoc/>
        public string CurrentDirectory => _currentDirectory;

        /// <inheritdoc/>
        public string CurrentDirectoryDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(_currentDirectory))
                    return "/";
                string root = GetDefaultRootPath();
                if (string.Equals(_currentDirectory, root, StringComparison.OrdinalIgnoreCase))
                    return "Internal storage";
                return _currentDirectory;
            }
        }

        /// <inheritdoc/>
        public bool CanNavigateUp
        {
            get
            {
                string root = GetDefaultRootPath();
                if (string.IsNullOrEmpty(_currentDirectory) || string.IsNullOrEmpty(root))
                    return false;
                return !string.Equals(
                    Path.GetFullPath(_currentDirectory).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <inheritdoc/>
        public event Action StateChanged;

        /// <inheritdoc/>
        public DocumentContentModel ActiveDocument => _activeDocument;

        /// <inheritdoc/>
        public IReadOnlyList<(string Key, string Label, string Path)> GetStorageRoots()
        {
            var roots = new List<(string, string, string)>();
            string internalRoot = GetDefaultRootPath();
            if (!string.IsNullOrEmpty(internalRoot))
                roots.Add(("internal", "Storage", internalRoot));

            AddRootIfExists(roots, "documents", "Documents", AndroidEnv.DirectoryDocuments);
            AddRootIfExists(roots, "downloads", "Download", AndroidEnv.DirectoryDownloads);
            AddRootIfExists(roots, "books", "Books", "Books");
            return roots;
        }

        /// <inheritdoc/>
        public IReadOnlyList<DocumentItemModel> ListCurrentDirectory()
        {
            var result = new List<DocumentItemModel>();
            if (string.IsNullOrEmpty(_currentDirectory) || !Directory.Exists(_currentDirectory))
                return result;

            try
            {
                foreach (string dir in Directory.EnumerateDirectories(_currentDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    DirectoryInfo info = new DirectoryInfo(dir);
                    result.Add(new DocumentItemModel
                    {
                        Path = dir,
                        Name = info.Name,
                        Kind = DocumentKind.Folder,
                        ModifiedLocal = info.LastWriteTime,
                    });
                }

                foreach (string file in Directory.EnumerateFiles(_currentDirectory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    DocumentKind kind = DocumentTextExtractor.DetectKind(file);
                    if (kind == DocumentKind.Other)
                        continue;

                    FileInfo info = new FileInfo(file);
                    result.Add(new DocumentItemModel
                    {
                        Path = file,
                        Name = info.Name,
                        Kind = kind,
                        SizeBytes = info.Length,
                        ModifiedLocal = info.LastWriteTime,
                    });
                }
            }
            catch (Exception)
            {
            }

            return result;
        }

        /// <inheritdoc/>
        public void EnterDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;
            _currentDirectory = path;
        }

        /// <inheritdoc/>
        public void NavigateUp()
        {
            if (!CanNavigateUp)
                return;
            DirectoryInfo parent = Directory.GetParent(_currentDirectory);
            if (parent != null && parent.Exists)
                _currentDirectory = parent.FullName;
        }

        /// <inheritdoc/>
        public void GoToRoot(string rootKey)
        {
            var roots = GetStorageRoots();
            var match = roots.FirstOrDefault(r => string.Equals(r.Key, rootKey, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Path) && Directory.Exists(match.Path))
                _currentDirectory = match.Path;
        }

        /// <inheritdoc/>
        public async Task OpenAsync(DocumentItemModel item)
        {
            if (item == null)
                return;
            await OpenUriAsync(item.Path, item.MimeType, item.Name);
        }

        /// <inheritdoc/>
        public async Task OpenUriAsync(string uriOrPath, string mimeType = null, string displayName = null)
        {
            if (string.IsNullOrEmpty(uriOrPath))
                return;

            DocumentKind kind = DocumentTextExtractor.DetectKind(uriOrPath, mimeType);
            string title = displayName ?? GuessDisplayName(uriOrPath);

            if (kind == DocumentKind.Djvu || kind == DocumentKind.Other)
            {
                CloseDocumentInternal();
                await OpenExternallyAsync(uriOrPath, mimeType);
                _activeDocument = new DocumentContentModel
                {
                    Path = uriOrPath,
                    Title = title,
                    Kind = kind == DocumentKind.Other ? DocumentKind.Other : DocumentKind.Djvu,
                    RenderMode = DocumentRenderMode.External,
                    InfoMessage = kind == DocumentKind.Djvu
                        ? "Opened DjVu in an external viewer."
                        : "Opened in an external viewer.",
                };
                RaiseStateChanged();
                return;
            }

            if (kind == DocumentKind.Pdf)
            {
                await OpenPdfAsync(uriOrPath, title);
                return;
            }

            using (Stream stream = OpenReadStream(uriOrPath))
            {
                DocumentContentModel content = DocumentTextExtractor.Extract(stream, uriOrPath, mimeType, title);
                CloseDocumentInternal();
                _activeDocument = content;
            }

            if (_activeDocument != null && _activeDocument.RenderMode == DocumentRenderMode.External)
                await OpenExternallyAsync(uriOrPath, mimeType);

            RaiseStateChanged();
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public void CloseDocument()
        {
            CloseDocumentInternal();
            RaiseStateChanged();
        }

        /// <inheritdoc/>
        public Task GoToPdfPageAsync(int pageIndex)
        {
            if (_activeDocument == null || _pdfRenderer == null)
                return Task.CompletedTask;

            pageIndex = Math.Clamp(pageIndex, 0, _pdfRenderer.PageCount - 1);
            RenderPdfPage(pageIndex);
            RaiseStateChanged();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task PickAndOpenAsync()
        {
            Intent picker = new Intent(Intent.ActionOpenDocument);
            picker.AddCategory(Intent.CategoryOpenable);
            picker.SetType("*/*");
            picker.PutExtra(Intent.ExtraMimeTypes, new[]
            {
                "text/*",
                "application/pdf",
                "application/epub+zip",
                "application/x-fictionbook+xml",
                "application/fb2",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "application/msword",
                "application/vnd.ms-excel",
                "application/vnd.oasis.opendocument.text",
                "application/vnd.oasis.opendocument.spreadsheet",
                "image/vnd.djvu",
                "image/x-djvu",
                "application/octet-stream",
            });
            picker.AddFlags(ActivityFlags.GrantReadUriPermission);

            var result = await _activityResultAwaiter.StartActivityAndWaitForResult(
                _appContext.RootActivity, picker);
            if (result?.ResultCode != Result.Ok || result.Data?.Data == null)
                return;

            AndroidUri uri = result.Data.Data;
            try
            {
                _appContext.RootActivity.ContentResolver.TakePersistableUriPermission(
                    uri,
                    result.Data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission));
            }
            catch (Exception)
            {
            }

            string mime = _appContext.RootActivity.ContentResolver.GetType(uri);
            string name = QueryDisplayName(uri) ?? uri.LastPathSegment;
            await OpenUriAsync(uri.ToString(), mime, name);
        }

        /// <inheritdoc/>
        public Task OpenExternallyAsync(string uriOrPath = null, string mimeType = null)
        {
            string path = uriOrPath ?? _activeDocument?.Path;
            if (string.IsNullOrEmpty(path))
                return Task.CompletedTask;

            try
            {
                Context context = GetLaunchContext();
                AndroidUri uri;
                string mime = mimeType;

                if (path.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    uri = AndroidUri.Parse(path);
                }
                else
                {
                    Java.IO.File file = new Java.IO.File(path);
                    uri = FileProvider.GetUriForFile(
                        context,
                        context.PackageName + ".fileprovider",
                        file);
                    mime ??= GuessMimeFromPath(path);
                }

                Intent intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(uri, string.IsNullOrEmpty(mime) ? "*/*" : mime);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
                context.StartActivity(Intent.CreateChooser(intent, "Open with"));
            }
            catch (Exception)
            {
                throw;
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseDocumentInternal();
        }

        private async Task OpenPdfAsync(string uriOrPath, string title)
        {
            CloseDocumentInternal();

            ParcelFileDescriptor fd = OpenParcelFd(uriOrPath);
            if (fd == null)
                throw new InvalidOperationException("Could not open PDF.");

            try
            {
                _pdfFd = fd;
                _pdfRenderer = new PdfRenderer(fd);
                _activeDocument = new DocumentContentModel
                {
                    Path = uriOrPath,
                    Title = title,
                    Kind = DocumentKind.Pdf,
                    RenderMode = DocumentRenderMode.PdfPages,
                    PdfPageCount = _pdfRenderer.PageCount,
                    PdfPageIndex = 0,
                };
                RenderPdfPage(0);
            }
            catch (Exception)
            {
                CloseDocumentInternal();
                // Fallback: open externally if PdfRenderer fails.
                await OpenExternallyAsync(uriOrPath, "application/pdf");
                _activeDocument = new DocumentContentModel
                {
                    Path = uriOrPath,
                    Title = title,
                    Kind = DocumentKind.Pdf,
                    RenderMode = DocumentRenderMode.External,
                    InfoMessage = "Opened PDF in an external viewer.",
                };
            }

            RaiseStateChanged();
        }

        private void RenderPdfPage(int pageIndex)
        {
            if (_pdfRenderer == null || _activeDocument == null)
                return;

            pageIndex = Math.Clamp(pageIndex, 0, _pdfRenderer.PageCount - 1);
            using (PdfRenderer.Page page = _pdfRenderer.OpenPage(pageIndex))
            {
                // Scale for readable on-screen width (~1080px).
                int targetWidth = 1080;
                float scale = targetWidth / (float)Math.Max(1, page.Width);
                int w = Math.Max(1, (int)(page.Width * scale));
                int h = Math.Max(1, (int)(page.Height * scale));

                using (Bitmap bitmap = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888))
                {
                    bitmap.EraseColor(Color.White);
                    page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

                    using (var ms = new MemoryStream())
                    {
                        bitmap.Compress(Bitmap.CompressFormat.Png, 90, ms);
                        string b64 = Convert.ToBase64String(ms.ToArray());
                        _activeDocument.PdfPageDataUrls = new List<string>
                        {
                            "data:image/png;base64," + b64,
                        };
                    }
                }
            }

            _activeDocument.PdfPageIndex = pageIndex;
            _activeDocument.PdfPageCount = _pdfRenderer.PageCount;
        }

        private Stream OpenReadStream(string uriOrPath)
        {
            if (uriOrPath.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
                uriOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                AndroidUri uri = AndroidUri.Parse(uriOrPath);
                Stream stream = _appContext.Context.ContentResolver.OpenInputStream(uri);
                if (stream == null)
                    throw new InvalidOperationException("Could not open stream.");
                // Buffer so ZipArchive / XML can seek.
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                stream.Dispose();
                ms.Position = 0;
                return ms;
            }

            return File.OpenRead(uriOrPath);
        }

        private ParcelFileDescriptor OpenParcelFd(string uriOrPath)
        {
            try
            {
                if (uriOrPath.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
                    uriOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    AndroidUri uri = AndroidUri.Parse(uriOrPath);
                    return _appContext.Context.ContentResolver.OpenFileDescriptor(uri, "r");
                }

                return ParcelFileDescriptor.Open(
                    new Java.IO.File(uriOrPath),
                    ParcelFileMode.ReadOnly);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void CloseDocumentInternal()
        {
            try
            {
                _pdfRenderer?.Close();
            }
            catch (Exception)
            {
            }
            _pdfRenderer = null;

            try
            {
                _pdfFd?.Close();
            }
            catch (Exception)
            {
            }
            _pdfFd = null;
            _activeDocument = null;
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }

        private Context GetLaunchContext()
        {
            return (Context)_appContext.RootActivity ?? _appContext.Context;
        }

        private static string GetDefaultRootPath()
        {
            try
            {
                Java.IO.File dir = AndroidEnv.ExternalStorageDirectory;
                return dir?.AbsolutePath ?? "/storage/emulated/0";
            }
            catch (Exception)
            {
                return "/storage/emulated/0";
            }
        }

        private static void AddRootIfExists(
            List<(string Key, string Label, string Path)> roots,
            string key,
            string label,
            string relativeOrAbsolute)
        {
            try
            {
                string path = relativeOrAbsolute;
                if (!Path.IsPathRooted(path))
                {
                    string root = GetDefaultRootPath();
                    path = Path.Combine(root, relativeOrAbsolute);
                    // Also try Environment public directory API when relative is a known type.
                    try
                    {
                        Java.IO.File pub = AndroidEnv.GetExternalStoragePublicDirectory(relativeOrAbsolute);
                        if (pub != null && pub.Exists())
                            path = pub.AbsolutePath;
                    }
                    catch (Exception)
                    {
                    }
                }

                if (Directory.Exists(path))
                    roots.Add((key, label, path));
            }
            catch (Exception)
            {
            }
        }

        private string QueryDisplayName(AndroidUri uri)
        {
            try
            {
                using (var cursor = _appContext.Context.ContentResolver.Query(
                    uri, new[] { IOpenableColumns.DisplayName }, null, null, null))
                {
                    if (cursor != null && cursor.MoveToFirst())
                    {
                        int idx = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                        if (idx >= 0)
                            return cursor.GetString(idx);
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static string GuessDisplayName(string uriOrPath)
        {
            if (string.IsNullOrEmpty(uriOrPath))
                return "Document";
            try
            {
                string name = Path.GetFileName(uriOrPath);
                if (!string.IsNullOrEmpty(name))
                    return AndroidUri.Decode(name);
            }
            catch (Exception)
            {
            }
            return uriOrPath;
        }

        private static string GuessMimeFromPath(string path)
        {
            DocumentKind kind = DocumentTextExtractor.DetectKind(path);
            return kind switch
            {
                DocumentKind.Pdf => "application/pdf",
                DocumentKind.Djvu => "image/vnd.djvu",
                DocumentKind.Ebook when path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)
                    => "application/epub+zip",
                DocumentKind.Ebook => "application/x-fictionbook+xml",
                DocumentKind.Text => "text/plain",
                DocumentKind.Office when path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                    => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                DocumentKind.Office when path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                    => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "*/*",
            };
        }

        /// <summary>Used by intent parsing to decide if a VIEW should go to the Reader tab.</summary>
        internal static DocumentKind DetectKind(string uriOrPath, string mimeType) =>
            DocumentTextExtractor.DetectKind(uriOrPath, mimeType);
    }
}
