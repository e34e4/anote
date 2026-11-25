// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using SilentNotes.Models;
using SilentNotes.Platforms;
using SilentNotes.Services;

namespace SilentNotes
{
    /// <summary>
    /// Home-screen widget showing the latest clipboard snippet with quick save to Scratchpad.
    /// </summary>
    [BroadcastReceiver(
        Name = "app.anote.ClipboardWidgetProvider",
        Label = "@string/widget_clipboard_label",
        Exported = true)]
    [IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate, ActionOpenClipboard, ActionSaveClipboard })]
    [MetaData("android.appwidget.provider", Resource = "@xml/widget_clipboard_info")]
    public class ClipboardWidgetProvider : AppWidgetProvider
    {
        public const string ActionOpenClipboard = "app.anote.clipboard_widget.OPEN";
        public const string ActionSaveClipboard = "app.anote.clipboard_widget.SAVE";

        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            if (context == null || appWidgetManager == null || appWidgetIds == null)
                return;

            string preview = ClipboardWidgetPersist.LoadPreviewText(context);
            foreach (int appWidgetId in appWidgetIds)
                UpdateWidget(context, appWidgetManager, appWidgetId, preview);
        }

        public override void OnReceive(Context context, Intent intent)
        {
            base.OnReceive(context, intent);
            if (context == null || intent?.Action == null)
                return;

            if (string.Equals(intent.Action, ActionOpenClipboard, StringComparison.Ordinal))
            {
                OpenClipboardTab(context);
                return;
            }

            if (string.Equals(intent.Action, ActionSaveClipboard, StringComparison.Ordinal))
            {
                bool saved = ClipboardWidgetPersist.SaveLatestToScratchpad(context);
                if (saved)
                {
                    AnoteToast.Show(context, "Saved to Scratchpad", FeedbackSeverity.Info);
                }
                else
                {
                    AnoteToast.Show(context, "No text clip to save", FeedbackSeverity.Warning);
                }

                RefreshAll(context);
            }
        }

        /// <summary>Refreshes every instance of the clipboard widget.</summary>
        public static void RefreshAll(Context context)
        {
            if (context == null)
                return;

            try
            {
                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(ClipboardWidgetProvider)));
                AppWidgetManager manager = AppWidgetManager.GetInstance(context);
                int[] ids = manager?.GetAppWidgetIds(component);
                if (ids == null || ids.Length == 0)
                    return;

                string preview = ClipboardWidgetPersist.LoadPreviewText(context);
                foreach (int id in ids)
                    UpdateWidget(context, manager, id, preview);
            }
            catch (Exception)
            {
            }
        }

        private static void OpenClipboardTab(Context context)
        {
            TabIntentRequest.Pending = new TabIntentRequest { TabIndex = 1 };

            var intent = new Intent(context, typeof(MainActivity));
            intent.PutExtra(FloatingButtonAccessibilityService.ExtraOpenTab, 1);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.ReorderToFront);
            context.StartActivity(intent);
        }

        private static void UpdateWidget(
            Context context,
            AppWidgetManager appWidgetManager,
            int appWidgetId,
            string preview)
        {
            var views = new RemoteViews(context.PackageName, Resource.Layout.widget_clipboard);
            views.SetTextViewText(Resource.Id.clipboard_widget_preview, preview ?? ClipboardWidgetPersist.EmptyPreviewMessage);

            var open = new Intent(context, typeof(ClipboardWidgetProvider));
            open.SetAction(ActionOpenClipboard);
            open.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
            var openPending = PendingIntent.GetBroadcast(
                context,
                appWidgetId,
                open,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            views.SetOnClickPendingIntent(Resource.Id.clipboard_widget_root, openPending);
            views.SetOnClickPendingIntent(Resource.Id.clipboard_widget_preview, openPending);
            views.SetOnClickPendingIntent(Resource.Id.clipboard_widget_title, openPending);

            var save = new Intent(context, typeof(ClipboardWidgetProvider));
            save.SetAction(ActionSaveClipboard);
            save.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
            var savePending = PendingIntent.GetBroadcast(
                context,
                200000 + appWidgetId,
                save,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            views.SetOnClickPendingIntent(Resource.Id.clipboard_widget_save, savePending);

            appWidgetManager.UpdateAppWidget(appWidgetId, views);
        }
    }
}