// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Android.Content;
using Android.Graphics;
using IOPath = System.IO.Path;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.ViewModels;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Executes the actions offered by the share target and the quick settings tile, without
    /// opening the full aNote UI.
    /// </summary>
    internal static class ShareTargetActions
    {
        private static readonly Regex UrlRegex = new Regex(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FeedLinkRegex = new Regex(
            @"<link\b[^>]*type\s*=\s*[""']application/(rss|atom)\+xml[""'][^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HrefRegex = new Regex(@"href\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public const int ReaderTabIndex = 15;
        public const string ExtraOpenDocumentPath = "aNoteOpenDocumentPath";
        public const string ExtraOpenDocumentMime = "aNoteOpenDocumentMime";

        public static void SaveTextToClipboard(Context context, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                int limit = Ioc.Instance.GetService<ISettingsService>()?.LoadSettingsOrDefault()?.ClipboardHistorySize ?? 500;
                Ioc.Instance.GetService<IClipboardHistoryService>()?.AddText(text.Trim(), limit);
                Ioc.Instance.GetService<IMessengerService>()?.Send(new ClipboardChangedMessage());
                Toast(context, "Saved to clipboard");
            }
            catch (Exception)
            {
                Toast(context, "Could not save to clipboard");
            }
        }

        public static void CreateNote(Context context, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                IRepositoryStorageService repositoryService = Ioc.Instance.GetService<IRepositoryStorageService>();
                repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                string title = FirstLine(text);
                CaptureNoteHelper.CreateTextNote(repository, title, text);
                bool saved = repositoryService.TrySaveRepository(repository);
                Ioc.Instance.GetService<IMessengerService>()?.Send(new NotesChangedMessage());
                Toast(context, saved ? "Saved to a new note" : "Could not save the note");
            }
            catch (Exception)
            {
                Toast(context, "Could not save the note");
            }
        }

        public static void CreateBookmark(Context context, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                string url = UrlRegex.Match(text)?.Value;
                if (string.IsNullOrWhiteSpace(url))
                {
                    Toast(context, "No link found in the shared text");
                    return;
                }

                url = UrlCheckUtils.CleanTrackingParams(url, out _);
                string title = text.Replace(url, string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(title))
                    title = new Uri(url).Host;

                IRepositoryStorageService repositoryService = Ioc.Instance.GetService<IRepositoryStorageService>();
                repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                repository.Bookmarks.Insert(0, new BookmarkModel
                {
                    Url = url,
                    Title = title,
                    Quote = string.Empty,
                    Note = string.Empty,
                });
                bool saved = repositoryService.TrySaveRepository(repository);
                Ioc.Instance.GetService<IMessengerService>()?.Send(new BookyChangedMessage());
                Toast(context, saved ? "Bookmark saved" : "Could not save the bookmark");
            }
            catch (Exception)
            {
                Toast(context, "Could not save the bookmark");
            }
        }

        public static async Task ProbeRssFeedAsync(Context context, string text)
        {
            string url = UrlRegex.Match(text ?? string.Empty)?.Value;
            if (string.IsNullOrWhiteSpace(url))
            {
                Toast(context, "No link found in the shared text");
                return;
            }

            try
            {
                string feedUrl = await ResolveFeedUrlAsync(url);
                if (string.IsNullOrWhiteSpace(feedUrl))
                {
                    Toast(context, "No RSS/Atom feed found at that link");
                    return;
                }

                string xml = await DownloadAsync(feedUrl);
                RssFeedParser.ParsedFeed parsed = RssFeedParser.Parse(xml);
                if (parsed == null)
                {
                    Toast(context, "The link is not a valid RSS/Atom feed");
                    return;
                }

                IRepositoryStorageService repositoryService = Ioc.Instance.GetService<IRepositoryStorageService>();
                repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                if (repository.RssFeeds.FindByUrl(feedUrl) == null)
                {
                    repository.RssFeeds.Add(new RssFeedModel
                    {
                        Url = feedUrl,
                        Title = string.IsNullOrWhiteSpace(parsed.Title) ? new Uri(feedUrl).Host : parsed.Title,
                        SiteUrl = parsed.SiteUrl,
                    });
                    repositoryService.TrySaveRepository(repository);
                }

                Toast(context, "RSS feed added");
            }
            catch (Exception)
            {
                Toast(context, "Could not add the RSS feed");
            }
        }

        public static byte[] ReadAllBytes(Context context, global::Android.Net.Uri uri)
        {
            if (context == null || uri == null)
                return null;
            try
            {
                using (Stream stream = context.ContentResolver.OpenInputStream(uri))
                using (var memory = new MemoryStream())
                {
                    stream?.CopyTo(memory);
                    return memory.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetDisplayName(Context context, global::Android.Net.Uri uri, string fallback)
        {
            try
            {
                string name = uri?.LastPathSegment;
                return string.IsNullOrWhiteSpace(name) ? fallback : IOPath.GetFileName(name);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public static async Task SaveImageToCameraAsync(Context context, byte[] bytes)
        {
            try
            {
                ICameraService camera = Ioc.Instance.GetService<ICameraService>();
                CameraPhotoModel photo = camera == null
                    ? null
                    : await camera.ConvertExistingImageAsync(bytes, "jpg", 90, true);
                Toast(context, photo != null ? "Image saved to the Camera tab" : "Could not save the image");
            }
            catch (Exception)
            {
                Toast(context, "Could not save the image");
            }
        }

        public static void SaveImageToClipboard(Context context, byte[] bytes)
        {
            try
            {
                using (Bitmap decoded = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length))
                {
                    if (decoded == null)
                    {
                        Toast(context, "Could not read the image");
                        return;
                    }

                    byte[] png = EncodePng(decoded);
                    byte[] thumbnail = EncodePng(CreateThumbnail(decoded, 256));
                    int limit = Ioc.Instance.GetService<ISettingsService>()?.LoadSettingsOrDefault()?.ClipboardHistorySize ?? 500;
                    Ioc.Instance.GetService<IClipboardHistoryService>()?.AddImage(png, thumbnail, limit);
                    Ioc.Instance.GetService<IMessengerService>()?.Send(new ClipboardChangedMessage());
                    Toast(context, "Image saved to the clipboard history");
                }
            }
            catch (Exception)
            {
                Toast(context, "Could not save the image");
            }
        }

        public static void CreateImageNote(Context context, byte[] bytes)
        {
            try
            {
                using (Bitmap decoded = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length))
                {
                    if (decoded == null)
                    {
                        Toast(context, "Could not read the image");
                        return;
                    }

                    string dataUrl = VoiceCapturePersist.BitmapToJpegDataUrl(decoded);
                    if (string.IsNullOrEmpty(dataUrl))
                    {
                        Toast(context, "Could not read the image");
                        return;
                    }

                    IRepositoryStorageService repositoryService = Ioc.Instance.GetService<IRepositoryStorageService>();
                    repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                    var note = new NoteModel
                    {
                        NoteType = NoteType.Text,
                        HtmlContent = "<h1>Shared image</h1><p><img src=\"" + dataUrl + "\"/></p>",
                    };
                    repository.Notes.Insert(0, note);
                    repository.RefreshOrderModifiedAt();
                    bool saved = repositoryService.TrySaveRepository(repository);
                    Ioc.Instance.GetService<IMessengerService>()?.Send(new NotesChangedMessage());
                    Toast(context, saved ? "Image saved in a new note" : "Could not save the note");
                }
            }
            catch (Exception)
            {
                Toast(context, "Could not save the note");
            }
        }

        public static string SaveFileToReader(Context context, byte[] bytes, string fileName, string mimeType)
        {
            try
            {
                Directory.CreateDirectory(AnotePaths.Down);
                string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "shared_file" : fileName);
                string path = IOPath.Combine(AnotePaths.Down, safeName);
                int counter = 1;
                while (File.Exists(path))
                {
                    string name = IOPath.GetFileNameWithoutExtension(safeName);
                    string extension = IOPath.GetExtension(safeName);
                    path = IOPath.Combine(AnotePaths.Down, name + "_" + counter.ToString(CultureInfo.InvariantCulture) + extension);
                    counter++;
                }

                File.WriteAllBytes(path, bytes);
                DocumentIntentRequest.Pending = new DocumentIntentRequest
                {
                    UriOrPath = path,
                    MimeType = mimeType,
                    DisplayName = IOPath.GetFileName(path),
                };
                Ioc.Instance.GetService<IMessengerService>()?.Send(new DocumentIntentMessage());
                Toast(context, "File saved and opened in the Reader");
                return path;
            }
            catch (Exception)
            {
                Toast(context, "Could not save the file");
                return null;
            }
        }

        public static async Task RecognizeTextAsync(Context context, byte[] bytes)
        {
            try
            {
                IOcrService ocr = Ioc.Instance.GetService<IOcrService>();
                string text = ocr == null ? null : await ocr.RecognizeTextAsync(bytes);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Toast(context, "No text found in the image");
                    return;
                }

                if (context is global::Android.App.Activity activity && !activity.IsFinishing)
                {
                    activity.RunOnUiThread(() =>
                    {
                        var builder = new global::Android.App.AlertDialog.Builder(activity);
                        builder.SetTitle("Recognized text");
                        builder.SetMessage(text);
                        builder.SetPositiveButton("Copy", (sender, args) => SaveTextToClipboard(context, text));
                        builder.SetNeutralButton("New note", (sender, args) => CreateNote(context, text));
                        builder.SetNegativeButton("Close", (sender, args) => { });
                        builder.Show();
                    });
                }
                else
                {
                    SaveTextToClipboard(context, text);
                }
            }
            catch (Exception)
            {
                Toast(context, "Could not recognize text");
            }
        }

        private static async Task<string> ResolveFeedUrlAsync(string url)
        {
            string content = await DownloadAsync(url);
            if (RssFeedParser.Parse(content) != null)
                return url;

            Match link = FeedLinkRegex.Match(content);
            if (!link.Success)
                return null;

            Match href = HrefRegex.Match(link.Value);
            if (!href.Success)
                return null;

            string value = href.Groups[1].Value;
            return Uri.TryCreate(new Uri(url), value, out Uri absolute) ? absolute.ToString() : null;
        }

        private static async Task<string> DownloadAsync(string url)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Linux; Android 13) aNote/1.0");
                using (HttpResponseMessage response = await client.GetAsync(url))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }

        private static byte[] EncodePng(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Compress(Bitmap.CompressFormat.Png, 100, stream);
                return stream.ToArray();
            }
        }

        private static Bitmap CreateThumbnail(Bitmap source, int maxSize)
        {
            int edge = Math.Max(source.Width, source.Height);
            if (edge <= maxSize)
                return source;

            float scale = maxSize / (float)edge;
            return Bitmap.CreateScaledBitmap(
                source,
                Math.Max(1, (int)(source.Width * scale)),
                Math.Max(1, (int)(source.Height * scale)),
                true);
        }

        private static string SanitizeFileName(string name)
        {
            var builder = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ' ')
                    builder.Append(c);
                else
                    builder.Append('_');
            }
            string result = builder.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "shared_file" : result;
        }

        private static string FirstLine(string text)
        {
            string first = (text ?? string.Empty).Trim().Split('\n').FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(first))
                return "Captured";
            return first.Length > 60 ? first.Substring(0, 60) : first;
        }

        private static void Toast(Context context, string message)
        {
            AnoteToast.Show(context, message, FeedbackSeverity.Unknown);
        }
    }
}
