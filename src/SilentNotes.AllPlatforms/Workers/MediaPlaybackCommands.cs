// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Lets the Android media notification trigger "next"/"previous" without knowing the Player
    /// view model. The Player view model registers its queue commands here.
    /// </summary>
    public static class MediaPlaybackCommands
    {
        /// <summary>Gets or sets the action which plays the next item of the queue.</summary>
        public static Action Next { get; set; }

        /// <summary>Gets or sets the action which plays the previous item of the queue.</summary>
        public static Action Previous { get; set; }

        /// <summary>Invokes the next action, if registered.</summary>
        public static bool TryNext()
        {
            Action action = Next;
            if (action == null)
                return false;
            action();
            return true;
        }

        /// <summary>Invokes the previous action, if registered.</summary>
        public static bool TryPrevious()
        {
            Action action = Previous;
            if (action == null)
                return false;
            action();
            return true;
        }
    }
}
