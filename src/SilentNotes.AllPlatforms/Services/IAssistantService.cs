// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Lets the user opt in to use aNote as the system digital assistant (assist gesture),
    /// publish launcher shortcuts, and open related system settings.
    /// </summary>
    public interface IAssistantService
    {
        /// <summary>Gets a value indicating whether this platform supports becoming the default assistant.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether aNote currently holds the assistant role.</summary>
        bool IsDefaultAssistant { get; }

        /// <summary>
        /// Opens the system UI to request that aNote becomes the default assistant app
        /// (home long-press / assist gesture on most devices).
        /// </summary>
        Task<bool> RequestDefaultAssistantAsync();

        /// <summary>
        /// Opens system voice-input / assistant settings so the user can bind home long-press
        /// (and related CrDroid/Lineage gestures) to aNote.
        /// </summary>
        void OpenAssistantSettings();

        /// <summary>
        /// Publishes launcher long-press shortcuts (Voice, Notes, Clipboard, Rec, Abro).
        /// </summary>
        bool PublishLauncherShortcuts();
    }
}
