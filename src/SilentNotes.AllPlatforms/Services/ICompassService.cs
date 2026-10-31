// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes.Services
{
    /// <summary>
    /// Magnetic / rotation compass readings for the Clock → Compass mode.
    /// </summary>
    public interface ICompassService
    {
        /// <summary>Gets a value indicating whether a compass sensor is available.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether listening is active.</summary>
        bool IsListening { get; }

        /// <summary>
        /// Gets magnetic heading in degrees clockwise from true/magnetic north (0–360),
        /// or null when no reading is available yet.
        /// </summary>
        float? HeadingDegrees { get; }

        /// <summary>Gets accuracy hint text, or null.</summary>
        string AccuracyText { get; }

        /// <summary>Raised when heading or accuracy changes.</summary>
        event Action ReadingChanged;

        /// <summary>Starts listening to orientation sensors.</summary>
        void Start();

        /// <summary>Stops listening to orientation sensors.</summary>
        void Stop();
    }
}