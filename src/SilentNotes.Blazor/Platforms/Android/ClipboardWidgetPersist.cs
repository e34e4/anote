// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using Android.Content;
using Android.Util;
using AndroidX.Core.Content;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Lightweight clipboard preview + scratchpad save for the home-screen clipboard widget.
    /// </summary>
    internal static class ClipboardWidgetPersist
    {
        private const string LogTag = "aNoteClipWidget";
        private const string PrefsName = "anote_clipboard_widget";
        private const string KeyPreview = "latest_preview";
        private const string KeyFullText = "latest_text";
        private const int PreviewMaxChars = 220;

        public const string EmptyPreviewMessage = "Open aNote to refresh";

        /// <summary>Caches the latest text clip for widget reads when Ioc is unavailable.</summary>
        public static void CacheLatestText(Context context, string text)
        {
            if (context == null || string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                ISharedPreferences prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                ISharedPreferencesEditor editor = prefs.Edit();
                editor.PutString(KeyFullText, text);
                editor.PutString(KeyPreview, TruncatePreview(text));
                editor.Apply();
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "Cache failed: " + ex.Message);
            }
        }

        public static void CacheLatestImage(Context context)
        {
            if (context == null)
                return;

            try
            {
                ISharedPreferences prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                ISharedPreferencesEditor editor = prefs.Edit();
                editor.PutString(KeyFullText, string.Empty);
                editor.PutString(KeyPreview, "[Image]");
                editor.Apply();
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "Cache image failed: " + ex.Message);
            }
        }

        public static string LoadPreviewText(Context context)
        {
            string full = TryGetLatestClipboardText(context, out string previewFromHistory);
            if (!string.IsNullOrEmpty(previewFromHistory))
                return previewFromHistory;
            if (!string.IsNullOrEmpty(full))
                return TruncatePreview(full);

            try
            {
                string cached = context?.GetSharedPreferences(PrefsName, FileCreationMode.Private)?.GetString(KeyPreview, null);
                if (!string.IsNullOrWhiteSpace(cached))
                    return cached;
            }
            catch (Exception)
            {
            }

            return EmptyPreviewMessage;
        }

        public static bool SaveLatestToScratchpad(Context context)
        {
            string text = TryGetLatestClipboardText(context, out _);
            if (string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    text = context?.GetSharedPreferences(PrefsName, FileCreationMode.Private)?.GetString(KeyFullText, null);
                }
                catch (Exception)
                {
                }
            }

            if (string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                IRepositoryStorageService repo = ResolveRepository(context, out bool isolated);
                if (repo == null)
                    return false;

                if (isolated)
                    repo.ClearCache();
                RepositoryStorageLoadResult loadResult = repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                if (loadResult == RepositoryStorageLoadResult.InvalidRepository
                    || model == null
                    || Object.ReferenceEquals(NoteRepositoryModel.InvalidRepository, model))
                    return false;

                CaptureNoteHelper.AppendScratchpad(model, text);
                bool saved = repo.TrySaveRepository(model);

                try
                {
                    Ioc.Instance.GetService<IMessengerService>()?.Send(new NotesChangedMessage());
                }
                catch (Exception)
                {
                }

                return saved;
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "Save scratchpad failed: " + ex);
                return false;
            }
        }

        private static string TryGetLatestClipboardText(Context context, out string preview)
        {
            preview = null;
            if (context == null)
                return null;

            try
            {
                IClipboardHistoryService history = TryResolveClipboardHistory(context);
                ClipboardHistoryModel model = history?.LoadHistoryOrDefault();
                ClipboardItemModel item = model?.Items?.FirstOrDefault(i => i != null && !i.IsDeleted);
                if (item == null)
                    return TryReadHistoryFileDirect(context, out preview);

                if (item.Kind == ClipboardItemKind.Image)
                {
                    preview = "[Image]";
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(item.Text))
                {
                    preview = TruncatePreview(item.Text);
                    return item.Text;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "History read failed: " + ex.Message);
            }

            return TryReadHistoryFileDirect(context, out preview);
        }

        private static string TryReadHistoryFileDirect(Context context, out string preview)
        {
            preview = null;
            try
            {
                string dir = ContextCompat.GetNoBackupFilesDir(context)?.AbsolutePath;
                if (string.IsNullOrEmpty(dir))
                    return null;

                string path = Path.Combine(dir, ClipboardHistoryModel.HistoryFileName);
                if (!File.Exists(path))
                    return null;

                using var stream = File.OpenRead(path);
                var xml = System.Xml.Linq.XDocument.Load(stream);
                ClipboardHistoryModel model = XmlUtils.DeserializeFromXmlDocument<ClipboardHistoryModel>(xml);
                ClipboardItemModel item = model?.Items?.FirstOrDefault(i => i != null && !i.IsDeleted);
                if (item == null)
                    return null;

                if (item.Kind == ClipboardItemKind.Image)
                {
                    preview = "[Image]";
                    return null;
                }

                if (string.IsNullOrWhiteSpace(item.Text))
                    return null;

                preview = TruncatePreview(item.Text);
                return item.Text;
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "Direct history file read failed: " + ex.Message);
                return null;
            }
        }

        private static IClipboardHistoryService TryResolveClipboardHistory(Context context)
        {
            try
            {
                if (IsIocReady())
                    return Ioc.Instance.GetService<IClipboardHistoryService>();
            }
            catch (Exception)
            {
            }

            try
            {
                var services = new ServiceCollection();
                MauiProgram.RegisterSharedServices(services);
                MauiProgram.RegisterPlatformServices(services);
                ServiceProvider provider = services.BuildServiceProvider();
                provider.GetService<IAppContextService>()?.InitializeWithContextOnly(context.ApplicationContext ?? context);
                return provider.GetService<IClipboardHistoryService>();
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "Could not bootstrap clipboard history: " + ex.Message);
                return null;
            }
        }

        private static IRepositoryStorageService ResolveRepository(Context context, out bool isolated)
        {
            isolated = false;
            if (IsIocReady())
            {
                try
                {
                    return Ioc.Instance.GetService<IRepositoryStorageService>();
                }
                catch (Exception)
                {
                }
            }

            if (context == null)
                return null;

            try
            {
                var services = new ServiceCollection();
                MauiProgram.RegisterSharedServices(services);
                MauiProgram.RegisterPlatformServices(services);
                ServiceProvider provider = services.BuildServiceProvider();
                provider.GetService<IAppContextService>()?.InitializeWithContextOnly(context.ApplicationContext ?? context);
                isolated = true;
                return provider.GetService<IRepositoryStorageService>();
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "Could not bootstrap repository: " + ex);
                return null;
            }
        }

        private static bool IsIocReady()
        {
            try
            {
                return Ioc.Instance.GetService<IRepositoryStorageService>() != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string TruncatePreview(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string trimmed = text.Replace("\r\n", "\n").Trim();
            if (trimmed.Length <= PreviewMaxChars)
                return trimmed;

            return trimmed.Substring(0, PreviewMaxChars) + "…";
        }
    }
}