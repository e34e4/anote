// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android implementation that requests <see cref="global::Android.App.Roles.RoleManager.RoleAssistant"/>.
    /// </summary>
    internal class AssistantService : IAssistantService
    {
        private readonly IAppContextService _appContext;
        private readonly IActivityResultAwaiter _activityResultAwaiter;

        public AssistantService(
            IAppContextService appContextService,
            IActivityResultAwaiter activityResultAwaiter = null)
        {
            _appContext = appContextService;
            _activityResultAwaiter = activityResultAwaiter;
        }

        /// <inheritdoc/>
        public bool IsSupported => Build.VERSION.SdkInt >= BuildVersionCodes.Q;

        /// <inheritdoc/>
        public bool IsDefaultAssistant
        {
            get
            {
                try
                {
                    if (!IsSupported)
                        return false;

                    Context context = _appContext.Context;
                    if (context == null)
                        return false;

                    var roleManager = (global::Android.App.Roles.RoleManager)context.GetSystemService(Context.RoleService);
                    return roleManager != null
                        && roleManager.IsRoleHeld(global::Android.App.Roles.RoleManager.RoleAssistant);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> RequestDefaultAssistantAsync()
        {
            try
            {
                if (!IsSupported)
                    return false;

                Context context = _appContext.Context;
                Activity activity = _appContext.RootActivity;
                if (context == null)
                    return false;

                var roleManager = (global::Android.App.Roles.RoleManager)context.GetSystemService(Context.RoleService);
                if (roleManager == null
                    || !roleManager.IsRoleAvailable(global::Android.App.Roles.RoleManager.RoleAssistant))
                {
                    // Fallback: open voice-input / default-apps settings when the role API is unavailable.
                    try
                    {
                        var settings = new Intent(global::Android.Provider.Settings.ActionVoiceInputSettings);
                        settings.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(settings);
                    }
                    catch (Exception)
                    {
                        var settings = new Intent(global::Android.Provider.Settings.ActionManageDefaultAppsSettings);
                        settings.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(settings);
                    }
                    return IsDefaultAssistant;
                }

                Intent roleIntent = roleManager.CreateRequestRoleIntent(
                    global::Android.App.Roles.RoleManager.RoleAssistant);

                if (activity != null && _activityResultAwaiter != null)
                {
                    await _activityResultAwaiter.StartActivityAndWaitForResult(activity, roleIntent);
                }
                else
                {
                    roleIntent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(roleIntent);
                }
            }
            catch (Exception)
            {
            }

            return IsDefaultAssistant;
        }
    }
}
