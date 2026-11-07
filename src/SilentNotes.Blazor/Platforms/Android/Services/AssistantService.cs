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
    /// Android implementation that requests <see cref="global::Android.App.Roles.RoleManager.RoleAssistant"/>,
    /// opens assistant settings, and publishes launcher shortcuts.
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
                    OpenAssistantSettings();
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

                // Also refresh launcher shortcuts after the role prompt.
                PublishLauncherShortcuts();
            }
            catch (Exception)
            {
            }

            return IsDefaultAssistant;
        }

        /// <inheritdoc/>
        public void OpenAssistantSettings()
        {
            Context context = _appContext.Context;
            if (context == null)
                return;

            // Try the most specific screens first; CrDroid/Lineage may only honor some of them.
            string[] actions =
            {
                global::Android.Provider.Settings.ActionVoiceInputSettings,
                global::Android.Provider.Settings.ActionManageDefaultAppsSettings,
                global::Android.Provider.Settings.ActionApplicationSettings,
            };

            foreach (string action in actions)
            {
                try
                {
                    var intent = new Intent(action);
                    intent.AddFlags(ActivityFlags.NewTask);
                    context.StartActivity(intent);
                    return;
                }
                catch (Exception)
                {
                    // try next
                }
            }
        }

        /// <inheritdoc/>
        public bool PublishLauncherShortcuts()
        {
            try
            {
                Context context = _appContext.Context;
                return Platforms.AppShortcutPublisher.Publish(context);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
