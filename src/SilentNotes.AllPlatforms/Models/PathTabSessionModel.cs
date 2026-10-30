// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Xml.Serialization;

namespace SilentNotes.Models
{
    /// <summary>
    /// One browsable path session (sub-tab) for File / Player / Reader.
    /// </summary>
    public class PathTabSessionModel
    {
        /// <summary>Gets or sets the absolute or relative path of this session.</summary>
        [XmlAttribute("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>Gets or sets a short label shown on the session chip.</summary>
        [XmlAttribute("label")]
        public string Label { get; set; } = string.Empty;
    }
}
