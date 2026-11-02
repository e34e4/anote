// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// Process-wide gate so closing Abro does not immediately re-open via the Abro tab auto-launch.
    /// </summary>
    public static class AbroLaunchGate
    {
        /// <summary>
        /// When true, the Abro tab should not auto-open the browser because the user just dismissed it.
        /// Cleared when Present is requested intentionally.
        /// </summary>
        public static bool SuppressAutoPresent { get; set; }
    }
}
