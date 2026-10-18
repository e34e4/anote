// Copyright © 2018 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Provider;
using AndroidX.Browser.CustomTabs;
using SilentNotes.Services;

using Uri = Android.Net.Uri;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Implementation of the <see cref="INativeBrowserService"/> interface for the Android platform.
    /// Supports registering aNote as the system's default browser (URLCheck overlay mode).
    /// </summary>
    internal class NativeBrowserService : INativeBrowserService
    {
        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;

        /// <summary>
        /// Initializes a new instance of the <see cref="NativeBrowserService"/> class.
        /// </summary>
        /// <param name="appContextService">Service to get the Android application context.</param>
        /// <param name="activityResultAwaiter">Optional activity result awaiter for role requests.</param>
        public NativeBrowserService(
            IAppContextService appContextService,
            IActivityResultAwaiter activityResultAwaiter = null)
        {
            _appContext = appContextService;
            _activityResultAwaiter = activityResultAwaiter;
        }

        /// <inheritdoc/>
        public bool CanSetDefaultBrowser => true;

        /// <inheritdoc/>
        public bool IsDefaultBrowserApp
        {
            get
            {
                try
                {
                    Context context = _appContext.Context;
                    if (context == null)
                        return false;

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                    {
                        var roleManager = (global::Android.App.Roles.RoleManager)context.GetSystemService(Context.RoleService);
                        if (roleManager != null && roleManager.IsRoleHeld(global::Android.App.Roles.RoleManager.RoleBrowser))
                            return true;
                    }

                    var probeIntent = new Intent(Intent.ActionView, Uri.Parse("https://example.com"));
                    ResolveInfo resolved = context.PackageManager?.ResolveActivity(probeIntent, PackageInfoFlags.MatchDefaultOnly);
                    return string.Equals(resolved?.ActivityInfo?.PackageName, context.PackageName, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestDefaultBrowserAppAsync()
        {
            try
            {
                Context context = _appContext.Context;
                Activity activity = _appContext.RootActivity;
                if (context == null)
                    return false;

                Intent roleIntent = null;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                {
                    var roleManager = (global::Android.App.Roles.RoleManager)context.GetSystemService(Context.RoleService);
                    if (roleManager != null && roleManager.IsRoleAvailable(global::Android.App.Roles.RoleManager.RoleBrowser))
                    {
                        roleIntent = roleManager.CreateRequestRoleIntent(global::Android.App.Roles.RoleManager.RoleBrowser);
                    }
                }

                if (roleIntent != null && activity != null && _activityResultAwaiter != null)
                {
                    await _activityResultAwaiter.StartActivityAndWaitForResult(activity, roleIntent);
                }
                else if (roleIntent != null)
                {
                    roleIntent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(roleIntent);
                }

                if (!IsDefaultBrowserApp)
                {
                    var settingsIntent = new Intent(Settings.ActionManageDefaultAppsSettings);
                    settingsIntent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(settingsIntent);
                }
            }
            catch (Exception)
            {
            }
            return IsDefaultBrowserApp;
        }

        /// <inheritdoc/>
        public void OpenWebsite(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            Context context = _appContext.RootActivity ?? _appContext.Context;
            if (IsDefaultBrowserApp)
            {
                var checkIntent = new Intent(context, typeof(UrlCheckActivity));
                checkIntent.SetAction(Intent.ActionView);
                checkIntent.SetData(Uri.Parse(url));
                checkIntent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(checkIntent);
                return;
            }

            Uri webpage = Uri.Parse(url);
            Intent intent = new Intent(Intent.ActionView, webpage);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }

        /// <inheritdoc/>
        public void OpenWebsiteInNewTab(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            Context context = _appContext.RootActivity ?? _appContext.Context;
            if (IsDefaultBrowserApp)
            {
                var checkIntent = new Intent(context, typeof(UrlCheckActivity));
                checkIntent.SetAction(Intent.ActionView);
                checkIntent.SetData(Uri.Parse(url));
                checkIntent.PutExtra(global::Android.Provider.Browser.ExtraCreateNewTab, true);
                checkIntent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(checkIntent);
                return;
            }

            Uri webpage = Uri.Parse(url);
            Intent intent = new Intent(Intent.ActionView, webpage);
            intent.AddCategory(Intent.CategoryBrowsable);
            intent.PutExtra(global::Android.Provider.Browser.ExtraApplicationId, _appContext.Context.PackageName);
            intent.PutExtra(global::Android.Provider.Browser.ExtraCreateNewTab, true);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }

        /// <inheritdoc/>
        public void OpenWebsiteInApp(string url)
        {
            // Use the Android custom tabs to display the webpage inside the app.
            CustomTabsIntent.Builder builder = new CustomTabsIntent.Builder();
            CustomTabsIntent customTabsIntent = builder.Build();
            customTabsIntent.LaunchUrl(_appContext.RootActivity, Uri.Parse(url));
        }
    }
}
