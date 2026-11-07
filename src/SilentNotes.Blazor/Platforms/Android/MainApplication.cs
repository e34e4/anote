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
		}

		public override void OnCreate()
		{
			base.OnCreate();
			EnsureMlKitInitialized(this);
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
