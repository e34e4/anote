// Copyright © 2023 Martin Stoeckli / aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Runtime;
using Xamarin.Google.MLKit.Common;
using Xamarin.Google.MLKit.Common.SDKInternal;

namespace SilentNotes
{
	[Application]
	public class MainApplication : MauiApplication
	{
		public MainApplication(IntPtr handle, JniHandleOwnership ownership)
			: base(handle, ownership)
		{
			// Must run before any WTelegram.Client is constructed (including from background
			// services). DefaultLogger uses Console.ForegroundColor → Arg_PlatformNotSupported.
			InstallWTelegramLogger();
		}

		public override void OnCreate()
		{
			InstallWTelegramLogger();
			base.OnCreate();
			EnsureMlKitInitialized(this);
		}

		/// <summary>
		/// Redirect WTelegram logs to logcat. Safe to call repeatedly.
		/// </summary>
		internal static void InstallWTelegramLogger()
		{
			WTelegram.Helpers.Log = (level, message) =>
			{
				try
				{
					Android.Util.LogPriority priority = level switch
					{
						0 => Android.Util.LogPriority.Verbose,
						1 => Android.Util.LogPriority.Debug,
						2 => Android.Util.LogPriority.Info,
						3 => Android.Util.LogPriority.Warn,
						_ => Android.Util.LogPriority.Error,
					};
					Android.Util.Log.WriteLine(priority, "WTelegram", message ?? string.Empty);
				}
				catch
				{
				}
			};
		}

		/// <summary>
		/// ML Kit's InitProvider is merged into the APK, but on .NET Android it can fail to
		/// initialize before the first barcode client is created. Manual init is idempotent via
		/// <see cref="MlKitContext.InitializeIfNeeded(Android.Content.Context)"/>.
		/// </summary>
		internal static void EnsureMlKitInitialized(Android.Content.Context context)
		{
			if (context == null)
				return;

			try
			{
				Android.Content.Context appContext = context.ApplicationContext ?? context;
				MlKitContext.InitializeIfNeeded(appContext);
			}
			catch (Exception)
			{
				try
				{
					MlKit.Initialize(context.ApplicationContext ?? context);
				}
				catch (Exception)
				{
				}
			}
		}

		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
	}
}
