// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace SilentNotes.Services
{
    /// <summary>
    /// Lets the user opt in to use aNote as the system digital assistant (assist gesture).
    /// </summary>
    public interface IAssistantService
    {
        /// <summary>Gets a value indicating whether this platform supports becoming the default assistant.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether aNote currently holds the assistant role.</summary>
        bool IsDefaultAssistant { get; }

        /// <summary>
        /// Opens the system UI to request that aNote becomes the default assistant app.
        /// </summary>
        Task<bool> RequestDefaultAssistantAsync();
    }
}
