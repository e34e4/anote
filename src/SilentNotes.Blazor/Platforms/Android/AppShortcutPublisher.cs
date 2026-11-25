// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Android.Content;
using Android.OS;
using AndroidX.Core.Content.PM;
using AndroidX.Core.Graphics.Drawable;
using SilentNotes.ViewModels;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Publishes launcher long-press shortcuts for common aNote entry points (Voice first).
    /// </summary>
    internal static class AppShortcutPublisher
    {
        public const string ExtraShortcutAction = "aNoteShortcutAction";
        public const string ActionVoice = "voice";
        public const string ActionNewNote = "newnote";
        public const string ActionNotes = "notes";
        public const string ActionClipboard = "clipboard";
        public const string ActionRss = "rss";

        /// <summary>Publishes dynamic shortcuts; returns false when the OS rejects them.</summary>
        public static bool Publish(Context context)
        {
            if (context == null || Build.VERSION.SdkInt < BuildVersionCodes.NMr1)
                return false;

            try
            {
                var shortcuts = new List<ShortcutInfoCompat>
                {
                    BuildShortcut(context, ActionVoice, "Voice", "Capture voice + screenshot", VoiceViewModel.TabIndex, '#', unchecked((int)0xFF4285F4)),
                    BuildNewNoteShortcut(context),
                    BuildShortcut(context, ActionNotes, "Notes", "Open notes", 0, 'N', unchecked((int)0xFFFBC02D)),
                    BuildShortcut(context, ActionClipboard, "Clipboard", "Open clipboard history", 1, 'C', unchecked((int)0xFF66BB6A)),
                    BuildShortcut(context, ActionRss, "RSS", "Open unread RSS", RssViewModel.TabIndex, 'R', unchecked((int)0xFFFF7043)),
                };

                ShortcutManagerCompat.RemoveAllDynamicShortcuts(context);
                ShortcutManagerCompat.SetDynamicShortcuts(context, shortcuts);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static ShortcutInfoCompat BuildNewNoteShortcut(Context context)
        {
            var intent = new Intent(context, typeof(ScratchCaptureActivity));
            intent.SetAction(Intent.ActionMain);
            intent.PutExtra(ExtraShortcutAction, ActionNewNote);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);

            IconCompat icon = IconCompat.CreateWithBitmap(CreateGlyphBitmap('+', unchecked((int)0xFFFFA726)));
            return new ShortcutInfoCompat.Builder(context, ActionNewNote)
                .SetShortLabel("New Note")
                .SetLongLabel("Quick scratchpad capture")
                .SetIcon(icon)
                .SetIntent(intent)
                .Build();
        }

        private static ShortcutInfoCompat BuildShortcut(
            Context context,
            string actionId,
            string shortLabel,
            string longLabel,
            int tabIndex,
            char glyph,
            int colorArgb)
        {
            Intent intent;
            if (actionId == ActionVoice)
            {
                intent = new Intent(context, typeof(VoiceCaptureActivity));
                intent.SetAction(Intent.ActionMain);
            }
            else
            {
                intent = new Intent(context, typeof(MainActivity));
                intent.SetAction(Intent.ActionMain);
                intent.PutExtra(FloatingButtonAccessibilityService.ExtraOpenTab, tabIndex);
            }

            intent.PutExtra(ExtraShortcutAction, actionId);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);

            IconCompat icon = IconCompat.CreateWithBitmap(CreateGlyphBitmap(glyph, colorArgb));
            return new ShortcutInfoCompat.Builder(context, actionId)
                .SetShortLabel(shortLabel)
                .SetLongLabel(longLabel)
                .SetIcon(icon)
                .SetIntent(intent)
                .Build();
        }

        private static global::Android.Graphics.Bitmap CreateGlyphBitmap(char glyph, int colorArgb)
        {
            int size = 192;
            var bitmap = global::Android.Graphics.Bitmap.CreateBitmap(
                size, size, global::Android.Graphics.Bitmap.Config.Argb8888);
            var canvas = new global::Android.Graphics.Canvas(bitmap);
            var paint = new global::Android.Graphics.Paint { AntiAlias = true };
            paint.Color = new global::Android.Graphics.Color(colorArgb);
            canvas.DrawCircle(size / 2f, size / 2f, size / 2f, paint);
            paint.Color = global::Android.Graphics.Color.White;
            paint.TextAlign = global::Android.Graphics.Paint.Align.Center;
            paint.TextSize = size * 0.48f;
            paint.FakeBoldText = true;
            float y = (size / 2f) - ((paint.Descent() + paint.Ascent()) / 2f);
            canvas.DrawText(glyph.ToString(), size / 2f, y, paint);
            return bitmap;
        }
    }
}
