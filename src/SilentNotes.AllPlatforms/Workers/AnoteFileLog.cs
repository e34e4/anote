// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Appends diagnostic lines under <see cref="AnotePaths.LogDir"/> (Android: <c>/sdcard/anote/log</c>).
    /// </summary>
    public static class AnoteFileLog
    {
        private static readonly object Gate = new object();

        /// <summary>Appends a line to <c>{LogDir}/{name}.log</c>.</summary>
        public static void Append(string name, string message)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "anote";

            try
            {
                AnotePaths.EnsureDirectories();
                string safeName = Path.GetFileName(name.Trim());
                if (string.IsNullOrEmpty(safeName))
                    safeName = "anote";
                string path = Path.Combine(AnotePaths.LogDir, safeName + ".log");
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (message ?? string.Empty);
                lock (Gate)
                {
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
