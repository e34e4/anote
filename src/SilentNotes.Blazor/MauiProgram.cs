// Copyright © 2023 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.Maui.LifecycleEvents;
using MudBlazor;
using MudBlazor.Services;
using SilentNotes.Models;
using SilentNotes.Platforms;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using SilentNotes.Workers;
using ZXing.Net.Maui.Controls;

namespace SilentNotes;

public static class MauiProgram
{
    // Shared, even when a new service provider is created in shutdown thread on Android (messenger
    // is not used in the shutdown thread).
    private static ISynchronizationState _synchronizationState;

    // Shared between the app container and the container built by the accessibility service, so
    // the clipboard monitor and the UI work on the same instance (they run in the same process).
    private static IClipboardHistoryService _clipboardHistory;

    /// <summary>
    /// Initializes the application and its IOC.
    /// </summary>
    /// <remarks>
    /// There are other configs which can be used for development purposes:
    /// - <see cref="NoteRepositoryModel"/> The name of the repository file is different for the
    ///   "debug" and the "release" environment, so that no real notes can be damaged on the device
    ///   or in the cloud storage when developing.
    /// - <see cref="SettingsModel"/> The name of the settings file is different for the "debug"
    ///   and the "release" environment, to not interfere.
    /// - <see cref="LanguageService"/> The language is fixed to "en" for the "debug" environment,
    ///   to generate english screenshots.
    /// </remarks>
    public static MauiApp CreateMauiApp()
    {
        // Workaround: Not necessary anymore since Maui .NET 9.
        // Fixes the restart problem on Android when closing the app with the back
        // button, possible since Maui 8.0.60.
        // AppContext.SetSwitch("BlazorWebView.AndroidFireAndForgetAsync", isEnabled: true);

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
            .ConfigureLifecycleEvents(events =>
            {
#if ANDROID
                // Registered LifecycleEvents are triggered not only by the MainActivity. To avoid
                // running them from other activities the MainActivity will trigger them directly.
#elif WINDOWS
                var applicationEventHandler = new ApplicationEventHandler();
                events.AddWindows(lifeCycleBuilder => lifeCycleBuilder
                    .OnWindowCreated((window) => applicationEventHandler.OnWindowCreated(window))
                    .OnClosed((window, args) => applicationEventHandler.OnClosed(window, args)));
                var thisAppInstance = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
                thisAppInstance.Activated += applicationEventHandler.OnRedirected;
#endif
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.HideTransitionDuration = 500;
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
            config.SnackbarConfiguration.ShowCloseIcon = true;
            config.SnackbarConfiguration.ShowTransitionDuration = 500;
            config.SnackbarConfiguration.SnackbarVariant = Variant.Text;
            config.SnackbarConfiguration.VisibleStateDuration = 6000;
        });

        RegisterSharedServices(builder.Services);
        RegisterPlatformServices(builder.Services);

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        //builder.Logging.AddDebug().SetMinimumLevel(LogLevel.Information);
#endif

#if ANDROID
        // Sleep/resume Blazor crashes were invisible in release logcat — route them there.
        DiagnosticLogging.Install();
        builder.Logging.AddProvider(DiagnosticLogging.CreateLoggerProvider());
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif

        MauiApp mauiApp = builder.Build();
        Ioc.Instance.Initialize(mauiApp.Services);
        return mauiApp;
    }

    internal static void RegisterSharedServices(IServiceCollection services)
    {
        services.AddSingleton<ISynchronizationState>((serviceProvider) => _synchronizationState ?? (_synchronizationState = new SynchronizationState(
            serviceProvider.GetService<IMessengerService>())));

        services.AddSingleton<IMessengerService>((serviceProvider) => new MessengerService());
        services.AddSingleton<ISvgIconService>((serviceProvider) => new SvgIconService());
        services.AddSingleton<ILanguageService>((serviceProvider) => new LanguageService(
            new LanguageServiceResourceReader(), "SilentNotes", new LanguageCodeService().GetSystemLanguageCode()));
        services.AddSingleton<INoteRepositoryUpdater>((serviceProvider) => new NoteRepositoryUpdater());
        services.AddSingleton<IThemeService>((serviceProvider) => new ThemeService(
            serviceProvider.GetService<ISettingsService>(),
            serviceProvider.GetService<IEnvironmentService>()));
        services.AddSingleton<IVersionService>((serviceProvider) => new VersionService());
        services.AddSingleton<ICloudStorageClientFactory>((serviceProvider) => new CloudStorageClientFactory());
        services.AddSingleton<IClipboardService>((serviceProvider) => new ClipboardService());
        services.AddSingleton<IClipboardSyncService>((serviceProvider) => new ClipboardSyncService(
            serviceProvider.GetService<IClipboardHistoryService>()));
        services.AddSingleton<IBookySyncService>((serviceProvider) => new BookySyncService(
            serviceProvider.GetService<ISettingsService>(),
            serviceProvider.GetService<IRepositoryStorageService>(),
            serviceProvider.GetService<ICloudStorageClientFactory>(),
            serviceProvider.GetService<IMessengerService>()));
        services.AddSingleton<IWebdavQuickSaveService>((serviceProvider) => new WebdavQuickSaveService(
            serviceProvider.GetService<IWebdavFileService>(),
            serviceProvider.GetService<ISettingsService>()));
        services.AddSingleton<IInternetStateService>((serviceProvider) => new InternetStateService());
        services.AddSingleton<ISafeKeyService>((serviceProvider) => new SafeKeyService());
        services.AddSingleton<IPasswordVaultService>((serviceProvider) => new PasswordVaultService(
            serviceProvider.GetService<IRepositoryStorageService>(),
            serviceProvider.GetService<ISettingsService>(),
            serviceProvider.GetService<ICryptoRandomService>()));
        services.AddSingleton<IPasswordVaultSyncService>((serviceProvider) => new PasswordVaultSyncService(
            serviceProvider.GetService<ISettingsService>(),
            serviceProvider.GetService<IPasswordVaultService>(),
            serviceProvider.GetService<ICloudStorageClientFactory>()));
        services.AddSingleton<IFontService>((serviceProvider) => new FontService());
        services.AddSingleton<IPhoneSpamService>((serviceProvider) => new PhoneSpamService(
            serviceProvider.GetService<ISettingsService>()));

        // Scoped services (some Blazor services like NavigationManager or IJSRuntime are scoped)
        // Workaround: It seems that scoped services are recreated when gotten from Ioc, therefore
        // we have to add them dynamically in MainLayout.razor.
        services.AddScoped<INavigationService>((serviceProvider) => new NavigationService(
            serviceProvider.GetService<NavigationManager>(),
            RouteNames.NoteRepository,
            serviceProvider.GetService<IMessengerService>()));
        services.AddScoped<INotificationService>((serviceProvider) => new NotificationService(
            serviceProvider.GetService<IFeedbackService>(),
            serviceProvider.GetService<ILanguageService>(),
            serviceProvider.GetService<ISettingsService>()));
    }

#if WINDOWS

    internal static void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<ICryptoRandomService>((serviceProvider) => new CryptoRandomService());
        services.AddSingleton<IDataProtectionService>((serviceProvider) => new DataProtectionService());
        services.AddSingleton<IXmlFileService>((serviceProvider) => new XmlFileService());
        services.AddSingleton<IEnvironmentService>((serviceProvider) => new EnvironmentService());
        services.AddSingleton<IClipboardHistoryService>((serviceProvider) => _clipboardHistory ??= new ClipboardHistoryService(
            serviceProvider.GetService<IXmlFileService>()));
        services.AddSingleton<IRepositoryStorageService>((serviceProvider) => new RepositoryStorageService(
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<ILanguageService>()));
        services.AddSingleton<ISettingsService>((serviceProvider) => new SettingsService(
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<IDataProtectionService>()));
        services.AddSingleton<INativeBrowserService>((serviceProvider) => new NativeBrowserService());
        services.AddSingleton<IFolderPickerService>((serviceProvider) => new FolderPickerService());
        services.AddSingleton<IFilePickerService>((serviceProvider) => new FilePickerService());
        services.AddSingleton<ISynchronizationService>((serviceProvider) => new SynchronizationService(
            serviceProvider.GetService<ISynchronizationState>()));
        services.AddSingleton<ISharingService>((serviceProvider) => new SharingService());
        services.AddSingleton<IDeviceCalendarService>((serviceProvider) => new DeviceCalendarService());
        services.AddSingleton<IClockService>((serviceProvider) => new ClockService(
            serviceProvider.GetService<IXmlFileService>()));
        services.AddSingleton<IDeviceSmsService>((serviceProvider) => new DeviceSmsService());
        services.AddSingleton<IImageFileService>((serviceProvider) => new ImageFileService());
        services.AddSingleton<IQrService>((serviceProvider) => new QrService());
        services.AddSingleton<IQrHistoryService>((serviceProvider) => new QrHistoryService(
            serviceProvider.GetService<IXmlFileService>()));
        services.AddSingleton<IAudioRecorderService>((serviceProvider) => new AudioRecorderService());
        services.AddSingleton<IWebdavFileService>((serviceProvider) => new WebdavFileService());
        services.AddSingleton<IDevicePhoneService>((serviceProvider) => new DevicePhoneService());
        services.AddSingleton<ICameraService>((serviceProvider) => new CameraService());
        services.AddSingleton<IMailService>((serviceProvider) => new MailService(
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<IDataProtectionService>(),
            serviceProvider.GetService<ICloudStorageClientFactory>()));
        services.AddSingleton<IMediaPlayerService>((serviceProvider) => new MediaPlayerService());
        services.AddSingleton<IDocumentReaderService>((serviceProvider) => new DocumentReaderService());
        services.AddSingleton<ITelegramService>((serviceProvider) => new TelegramService(
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<IDataProtectionService>(),
            serviceProvider.GetService<IInternetStateService>()));

        services.AddScoped<IFeedbackService>((serviceProvider) => new FeedbackService(
            serviceProvider.GetService<ISnackbar>(),
            serviceProvider.GetService<ILanguageService>()));
    }

#elif ANDROID

    internal static void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<IAppContextService>((serviceProvider) => new AppContextService());
        services.AddSingleton<ICryptoRandomService>((serviceProvider) => new CryptoRandomService());
        services.AddSingleton<IDataProtectionService>((serviceProvider) => new DataProtectionService(
            serviceProvider.GetService<ICryptoRandomService>()));
        services.AddSingleton<IXmlFileService>((serviceProvider) => new XmlFileService());
        services.AddSingleton<IEnvironmentService>((serviceProvider) => new EnvironmentService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IMessengerService>()));
        services.AddSingleton<IClipboardHistoryService>((serviceProvider) => _clipboardHistory ??= new ClipboardHistoryService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IXmlFileService>()));
        services.AddSingleton<IRepositoryStorageService>((serviceProvider) => new RepositoryStorageService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<ILanguageService>()));
        services.AddSingleton<ISettingsService>((serviceProvider) => new SettingsService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<IDataProtectionService>()));
        services.AddSingleton<IActivityResultAwaiter>((ServiceProvider) => new ActivityResultAwaiter());
        services.AddSingleton<INativeBrowserService>((serviceProvider) => new NativeBrowserService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<IFolderPickerService>((serviceProvider) => new FolderPickerService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<IFilePickerService>((serviceProvider) => new FilePickerService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<ISynchronizationService>((serviceProvider) => new SynchronizationService(
            serviceProvider.GetService<ISynchronizationState>()));
        services.AddSingleton<ISharingService>((serviceProvider) => new SharingService(
            serviceProvider.GetService<IAppContextService>()));
        services.AddSingleton<IDeviceCalendarService>((serviceProvider) => new DeviceCalendarService(
            serviceProvider.GetService<IAppContextService>()));
        services.AddSingleton<IClockService>((serviceProvider) => new ClockService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IXmlFileService>()));
        services.AddSingleton<IDeviceSmsService>((serviceProvider) => new DeviceSmsService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IRepositoryStorageService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<IImageFileService>((serviceProvider) => new ImageFileService(
            serviceProvider.GetService<IAppContextService>()));
        services.AddSingleton<IQrService>((serviceProvider) => new QrService(
            serviceProvider.GetService<IAppContextService>()));
        services.AddSingleton<IQrHistoryService>((serviceProvider) => new QrHistoryService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IXmlFileService>()));
        services.AddSingleton<IAudioRecorderService>((serviceProvider) => new AudioRecorderService(
            serviceProvider.GetService<IAppContextService>()));
        services.AddSingleton<IWebdavFileService>((serviceProvider) => new WebdavFileService(
            serviceProvider.GetService<IAppContextService>()));
        services.AddSingleton<IDevicePhoneService>((serviceProvider) => new DevicePhoneService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IRepositoryStorageService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<ICameraService>((serviceProvider) => new CameraService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<IMailService>((serviceProvider) => new MailService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<IDataProtectionService>(),
            serviceProvider.GetService<ICloudStorageClientFactory>()));
        services.AddSingleton<IMediaPlayerService>((serviceProvider) => new MediaPlayerService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<IDocumentReaderService>((serviceProvider) => new DocumentReaderService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IActivityResultAwaiter>()));
        services.AddSingleton<ITelegramService>((serviceProvider) => new TelegramService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<IXmlFileService>(),
            serviceProvider.GetService<IDataProtectionService>(),
            serviceProvider.GetService<IInternetStateService>()));

        services.AddScoped<IFeedbackService>((serviceProvider) => new FeedbackService(
            serviceProvider.GetService<IAppContextService>(),
            serviceProvider.GetService<ISnackbar>(),
            serviceProvider.GetService<ILanguageService>()));
    }

#endif
}
