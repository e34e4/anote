// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using Android.OS;
using Android.Systems;

namespace SilentNotes
{
    /// <summary>
    /// Runs native binaries shipped under app storage. On Android 10+ (API 29) the OS blocks
    /// direct <c>exec</c> of files in the writable app home directory (W^X). Invoking the system
    /// dynamic linker with an absolute binary path works around that restriction for dynamically
    /// linked ELF tools such as ffmpeg / yt-dlp.
    /// </summary>
    internal static class AnoteNativeExec
    {
        /// <summary>
        /// Builds an argv suitable for <see cref="Java.Lang.Runtime.Exec(string[])"/> that can
        /// run <paramref name="binaryPath"/> even from app-private storage on API 29+.
        /// </summary>
        public static string[] BuildCommand(string binaryPath, IEnumerable<string> arguments)
        {
            if (string.IsNullOrWhiteSpace(binaryPath))
                throw new ArgumentException("Binary path is required.", nameof(binaryPath));

            string absolute = Path.GetFullPath(binaryPath);
            EnsureExecutable(absolute);

            var cmd = new List<string>();
            if ((int)Build.VERSION.SdkInt >= 29)
            {
                string linker = ResolveLinkerPath();
                if (!string.IsNullOrEmpty(linker))
                {
                    cmd.Add(linker);
                    cmd.Add(absolute);
                }
                else
                {
                    cmd.Add(absolute);
                }
            }
            else
            {
                cmd.Add(absolute);
            }

            if (arguments != null)
                cmd.AddRange(arguments);

            return cmd.ToArray();
        }

        /// <summary>chmod 755 so freshly extracted / mirrored copies are runnable.</summary>
        public static void EnsureExecutable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                Os.Chmod(path, Convert.ToInt32("755", 8));
            }
            catch (Exception)
            {
                try
                {
                    Java.Lang.Runtime.GetRuntime()?.Exec(new[] { "chmod", "755", path })?.WaitFor();
                }
                catch (Exception)
                {
                }
            }
        }

        private static string ResolveLinkerPath()
        {
            try
            {
                // Prefer matching process bitness; fall back to whichever linker exists.
                if (IntPtr.Size >= 8 && File.Exists("/system/bin/linker64"))
                    return "/system/bin/linker64";
                if (File.Exists("/system/bin/linker"))
                    return "/system/bin/linker";
                if (File.Exists("/system/bin/linker64"))
                    return "/system/bin/linker64";
            }
            catch (Exception)
            {
            }

            return null;
        }
    }
}
