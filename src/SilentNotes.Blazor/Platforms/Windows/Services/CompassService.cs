// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using SilentNotes.Services;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Stub compass for Windows.
    /// </summary>
    internal class CompassService : ICompassService
    {
        /// <inheritdoc/>
        public event Action ReadingChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public bool IsListening => false;

        /// <inheritdoc/>
        public float? HeadingDegrees => null;

        /// <inheritdoc/>
        public string AccuracyText => null;

        /// <inheritdoc/>
        public void Start()
        {
        }

        /// <inheritdoc/>
        public void Stop()
        {
        }
    }
}