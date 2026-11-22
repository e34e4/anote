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
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Home-screen widget backed by the pinned "widget" note (black background, green text).
    /// </summary>
    [BroadcastReceiver(
        Name = "app.anote.WidgetAppWidgetProvider",
        Label = "@string/widget_label",
        Exported = true)]
    [IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate, ActionShowOpen, ActionOpenNote })]
    [MetaData("android.appwidget.provider", Resource = "@xml/widget_info")]
    public class WidgetAppWidgetProvider : AppWidgetProvider
    {
        public const string ActionShowOpen = "app.anote.widget.SHOW_OPEN";
        public const string ActionOpenNote = "app.anote.widget.OPEN_NOTE";

        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            if (context == null || appWidgetManager == null || appWidgetIds == null)
                return;

            string text = WidgetNotePersist.LoadText(context);
            foreach (int appWidgetId in appWidgetIds)
                UpdateWidget(context, appWidgetManager, appWidgetId, text, showActions: false);
        }

        public override void OnReceive(Context context, Intent intent)
        {
            base.OnReceive(context, intent);
            if (context == null || intent?.Action == null)
                return;

            if (string.Equals(intent.Action, ActionShowOpen, StringComparison.Ordinal))
            {
                int appWidgetId = intent.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId);
                if (appWidgetId == AppWidgetManager.InvalidAppwidgetId)
                    return;

                // Reveal the Open button, then open the black/green typing pad.
                string text = WidgetNotePersist.LoadText(context);
                AppWidgetManager manager = AppWidgetManager.GetInstance(context);
                UpdateWidget(context, manager, appWidgetId, text, showActions: true);

                var edit = new Intent(context, typeof(WidgetInputActivity));
                edit.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
                edit.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                context.StartActivity(edit);
                return;
            }

            if (string.Equals(intent.Action, ActionOpenNote, StringComparison.Ordinal))
            {
                OpenWidgetNote(context);
            }
        }

        public override void OnEnabled(Context context)
        {
            base.OnEnabled(context);
            WidgetNotePersist.EnsureAndGetNoteId(context);
        }

        /// <summary>Refreshes every instance of the widget with the current note text.</summary>
        public static void RefreshAll(Context context)
        {
            if (context == null)
                return;

            try
            {
                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(WidgetAppWidgetProvider)));
                AppWidgetManager manager = AppWidgetManager.GetInstance(context);
                int[] ids = manager?.GetAppWidgetIds(component);
                if (ids == null || ids.Length == 0)
                    return;

                string text = WidgetNotePersist.LoadText(context);
                foreach (int id in ids)
                    UpdateWidget(context, manager, id, text, showActions: false);
            }
            catch
            {
            }
        }

        internal static void OpenWidgetNote(Context context)
        {
            if (context == null)
                return;

            Guid? noteId = WidgetNotePersist.EnsureAndGetNoteId(context);
            if (noteId == null)
                return;

            string route = RouteNames.Combine(RouteNames.Note, noteId.Value);
            RouteIntentRequest.Pending = new RouteIntentRequest { Route = route };

            var intent = new Intent(context, typeof(MainActivity));
            intent.PutExtra(FloatingButtonAccessibilityService.ExtraOpenRoute, route);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.ReorderToFront);
            context.StartActivity(intent);
        }

        private static void UpdateWidget(
            Context context,
            AppWidgetManager appWidgetManager,
            int appWidgetId,
            string text,
            bool showActions)
        {
            var views = new RemoteViews(context.PackageName, Resource.Layout.widget_note);
            views.SetTextViewText(Resource.Id.widget_text, text ?? string.Empty);
            views.SetViewVisibility(
                Resource.Id.widget_actions,
                showActions ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone);

            var showOpen = new Intent(context, typeof(WidgetAppWidgetProvider));
            showOpen.SetAction(ActionShowOpen);
            showOpen.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
            var showOpenPending = PendingIntent.GetBroadcast(
                context,
                appWidgetId,
                showOpen,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            views.SetOnClickPendingIntent(Resource.Id.widget_text, showOpenPending);
            views.SetOnClickPendingIntent(Resource.Id.widget_root, showOpenPending);

            var openNote = new Intent(context, typeof(WidgetAppWidgetProvider));
            openNote.SetAction(ActionOpenNote);
            openNote.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
            var openPending = PendingIntent.GetBroadcast(
                context,
                100000 + appWidgetId,
                openNote,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            views.SetOnClickPendingIntent(Resource.Id.widget_open, openPending);

            appWidgetManager.UpdateAppWidget(appWidgetId, views);
        }
    }
}
