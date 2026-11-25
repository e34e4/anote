// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.Content;
using Android.Runtime;
using AndroidX.Concurrent.Futures;
using AndroidX.Work;
using Google.Common.Util.Concurrent;
using Java.Util.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Schedules bookmark dead-link checks with the Android <see cref="WorkManager"/>.
    /// </summary>
    internal sealed class BookmarkLinkCheckService : IBookmarkLinkCheckService
    {
        private readonly IAppContextService _appContext;

        public BookmarkLinkCheckService(IAppContextService appContext)
        {
            _appContext = appContext;
            EnsurePeriodicScheduled();
        }

        /// <inheritdoc/>
        public void EnqueueOneTimeCheck()
        {
            if (_appContext?.Context == null)
                return;

            Constraints constraints = new Constraints.Builder()
                .SetRequiredNetworkType(NetworkType.Connected)
                .Build();

            OneTimeWorkRequest workRequest = new OneTimeWorkRequest.Builder(typeof(BookmarkLinkCheckWorker))
                .AddTag(BookmarkLinkCheckWorker.TAG)
                .SetConstraints(constraints)
                .Build();

            WorkManager.GetInstance(_appContext.Context)
                .BeginUniqueWork(BookmarkLinkCheckWorker.TAG + "_onetime", ExistingWorkPolicy.Keep, workRequest)
                .Enqueue();
        }

        /// <inheritdoc/>
        public void EnsurePeriodicScheduled()
        {
            if (_appContext?.Context == null)
                return;

            Constraints constraints = new Constraints.Builder()
                .SetRequiredNetworkType(NetworkType.Connected)
                .Build();

            PeriodicWorkRequest periodic = new PeriodicWorkRequest.Builder(
                    typeof(BookmarkLinkCheckWorker),
                    7,
                    TimeUnit.Days)
                .AddTag(BookmarkLinkCheckWorker.TAG)
                .SetConstraints(constraints)
                .Build();

            WorkManager.GetInstance(_appContext.Context)
                .EnqueueUniquePeriodicWork(
                    BookmarkLinkCheckWorker.TAG + "_periodic",
                    ExistingPeriodicWorkPolicy.Keep,
                    periodic);
        }
    }

    /// <summary>
    /// WorkManager worker that checks all bookmark URLs and persists link status in the repository.
    /// </summary>
    public sealed class BookmarkLinkCheckWorker : ListenableWorker, CallbackToFutureAdapter.IResolver
    {
        public const string TAG = "a8f4c2e1-6b9d-4a3f-9c12-7e5d8b0f1a2c";
        private readonly Context _appContext;

        public BookmarkLinkCheckWorker(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
        }

        public BookmarkLinkCheckWorker(Context appContext, WorkerParameters workerParams)
            : base(appContext, workerParams)
        {
            _appContext = appContext;
        }

        public override IListenableFuture StartWork()
        {
            return CallbackToFutureAdapter.GetFuture(this);
        }

        public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
        {
            Task.Run(async () =>
            {
                try
                {
                    if (_appContext != null)
                        await RunBookmarkLinkCheckAsync(_appContext);
                }
                catch (Exception)
                {
                    // always signal success to the workmanager
                }

                completer.Set(Result.InvokeSuccess());
            });
            return TAG;
        }

        private static async Task RunBookmarkLinkCheckAsync(Context appContext)
        {
            ServiceCollection services = new ServiceCollection();
            MauiProgram.RegisterSharedServices(services);
            MauiProgram.RegisterPlatformServices(services);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IAppContextService appContextService = serviceProvider.GetService<IAppContextService>();
            appContextService?.InitializeWithContextOnly(appContext);

            IRepositoryStorageService repositoryStorage = serviceProvider.GetService<IRepositoryStorageService>();
            if (repositoryStorage == null)
                return;

            repositoryStorage.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            if (repository?.Bookmarks == null || repository.Bookmarks.Count == 0)
                return;

            await BookmarkLinkChecker.CheckBookmarksAsync(repository.Bookmarks);
            repositoryStorage.TrySaveRepository(repository);
        }
    }
}