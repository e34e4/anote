// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Models
{
    /// <summary>
    /// Carries a request (e.g. from the floating button quick menu) to navigate to a Blazor route.
    /// </summary>
    public class RouteIntentRequest
    {
        /// <summary>
        /// Gets or sets the pending request which should be handled by <c>MainLayout</c>.
        /// </summary>
        public static RouteIntentRequest Pending { get; set; }

        /// <summary>
        /// Gets or sets the route to open (e.g. "/system").
        /// </summary>
        public string Route { get; set; }
    }
}
