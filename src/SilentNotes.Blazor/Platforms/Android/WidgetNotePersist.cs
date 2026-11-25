// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.Content;
using Android.Util;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Loads and saves the pinned "widget" note from Android widget / input surfaces.
    /// </summary>
    internal static class WidgetNotePersist
    {
        private const string LogTag = "aNoteWidget";

        public static string LoadText(Context context)
        {
            try
            {
                IRepositoryStorageService repo = ResolveRepository(context, out bool isolated);
                if (repo == null)
                    return string.Empty;

                // Only clear cache on an isolated bootstrap provider. Clearing the live IoC
                // cache races the main UI and can reload a partial repository.
                if (isolated)
                    repo.ClearCache();
                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                NoteModel note = WidgetNoteHelper.EnsureWidgetNote(model, out bool changed);
                if (changed)
                    repo.TrySaveRepository(model);

                return WidgetNoteHelper.GetWidgetPlainText(note) ?? string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "Load failed: " + ex);
                return string.Empty;
            }
        }

        public static Guid? EnsureAndGetNoteId(Context context)
        {
            try
            {
                IRepositoryStorageService repo = ResolveRepository(context, out bool isolated);
                if (repo == null)
                    return null;

                if (isolated)
                    repo.ClearCache();
                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                NoteModel note = WidgetNoteHelper.EnsureWidgetNote(model, out bool changed);
                if (changed)
                    repo.TrySaveRepository(model);

                return note?.Id;
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "Ensure note failed: " + ex);
                return null;
            }
        }

        public static bool SaveText(Context context, string text)
        {
            try
            {
                IRepositoryStorageService repo = ResolveRepository(context, out bool isolated);
                if (repo == null)
                    return false;

                if (isolated)
                    repo.ClearCache();
                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                NoteModel note = WidgetNoteHelper.EnsureWidgetNote(model);
                WidgetNoteHelper.SetWidgetBody(note, text ?? string.Empty);
                bool saved = repo.TrySaveRepository(model);

                try
                {
                    IMessengerService messenger = TryGetMessenger();
                    messenger?.Send(new NotesChangedMessage());
                }
                catch
                {
                }

                return saved;
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "Save failed: " + ex);
                return false;
            }
        }

        private static IMessengerService TryGetMessenger()
        {
            try
            {
                if (!IsIocReady())
                    return null;
                return Ioc.Instance.GetService<IMessengerService>();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsIocReady()
        {
            try
            {
                // Probe a known service; Ioc throws when the MAUI host has not initialized yet.
                return Ioc.Instance.GetService<IRepositoryStorageService>() != null;
            }
            catch
            {
                return false;
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
                catch
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
    }
}
