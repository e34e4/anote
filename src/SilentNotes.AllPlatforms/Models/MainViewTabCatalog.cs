// Copyright © 2026 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// Metadata and default ordering for main view tab buttons (stable tab indices).
    /// </summary>
    public static class MainViewTabCatalog
    {
        /// <summary>Describes one main view tab for display.</summary>
        public readonly struct TabDescriptor
        {
            /// <summary>Initializes a new instance of the <see cref="TabDescriptor"/> struct.</summary>
            public TabDescriptor(int index, string emoji, string languageKey)
            {
                Index = index;
                Emoji = emoji;
                LanguageKey = languageKey;
            }

            /// <summary>Stable tab index used by gestures and intents.</summary>
            public int Index { get; }

            /// <summary>Emoji shown on the tab button.</summary>
            public string Emoji { get; }

            /// <summary>Localization key for the tab title.</summary>
            public string LanguageKey { get; }
        }

        private static readonly TabDescriptor[] Tabs =
        {
            new TabDescriptor(20, "🎤", "tab_voice"),
            new TabDescriptor(0, "📝", "tab_notes"),
            new TabDescriptor(1, "📋", "tab_clipboard"),
            new TabDescriptor(2, "🔖", "tab_booky"),
            new TabDescriptor(3, "👤", "tab_phone"),
            new TabDescriptor(18, "📰", "tab_rss"),
            new TabDescriptor(4, "🧮", "tab_calc"),
            new TabDescriptor(5, "📅", "tab_cald"),
            new TabDescriptor(6, "⏰", "tab_clock"),
            new TabDescriptor(7, "💬", "tab_sms"),
            new TabDescriptor(8, "🔳", "tab_qr"),
            new TabDescriptor(9, "🎙️", "tab_rec"),
            new TabDescriptor(10, "📁", "tab_file"),
            new TabDescriptor(11, "📞", "tab_dialer"),
            new TabDescriptor(12, "📷", "tab_camera"),
            new TabDescriptor(13, "✉️", "tab_mail"),
            new TabDescriptor(14, "▶️", "tab_player"),
            new TabDescriptor(15, "📖", "tab_reader"),
            new TabDescriptor(16, "🔑", "tab_password"),
            new TabDescriptor(17, "✈", "tab_tg"),
            new TabDescriptor(19, "🌐", "tab_abro"),
        };

        /// <summary>Gets the number of main view tabs.</summary>
        public static int TabCount
        {
            get { return Tabs.Length; }
        }

        /// <summary>
        /// Gets the highest stable tab index (for clamping intents). Not the same as
        /// <see cref="TabCount"/> - 1 because indices are sparse.
        /// </summary>
        public static int MaxTabIndex
        {
            get
            {
                int max = 0;
                foreach (TabDescriptor tab in Tabs)
                {
                    if (tab.Index > max)
                        max = tab.Index;
                }
                return max;
            }
        }

        /// <summary>Gets all tab descriptors in default visual order.</summary>
        public static IReadOnlyList<TabDescriptor> AllTabs
        {
            get { return Tabs; }
        }

        /// <summary>Gets tab metadata for a stable tab index, if known.</summary>
        public static bool TryGetTab(int tabIndex, out TabDescriptor descriptor)
        {
            foreach (TabDescriptor tab in Tabs)
            {
                if (tab.Index == tabIndex)
                {
                    descriptor = tab;
                    return true;
                }
            }

            descriptor = default;
            return false;
        }
    }
}
