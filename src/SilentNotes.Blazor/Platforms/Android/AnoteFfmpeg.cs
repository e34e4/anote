// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SilentNotes.Workers;

namespace SilentNotes
{
    /// <summary>
    /// Locates the optional ffmpeg binary under <see cref="AnotePaths.FfmpegDir"/>.
    /// Cut and convert in <see cref="VideoEditorActivity"/> require this binary for reliable quality.
    /// </summary>
    internal static class AnoteFfmpeg
    {
        public static string FfmpegDir => AnotePaths.FfmpegDir;

        /// <summary>Returns the absolute path to an executable ffmpeg binary, or null if missing.</summary>
        public static string FindBinary()
        {
            // Prefer app-private copy (executable); /sdcard is often noexec.
            try
            {
                string exec = Path.Combine(
                    Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "mediatools", "ffmpeg");
                if (File.Exists(exec))
                    return exec;
            }
            catch (Exception)
            {
            }

            string resolved = AnotePaths.ResolveFfmpegBinary();
            if (!string.IsNullOrEmpty(resolved))
                return resolved;

            string[] names = { "ffmpeg", "ffmpeg_arm64", "ffmpeg_armv7", "ffmpeg_x86_64" };
            foreach (string name in names)
            {
                string path = Path.Combine(FfmpegDir, name);
                if (File.Exists(path))
                    return path;
            }

            try
            {
                if (Directory.Exists(FfmpegDir))
                {
                    foreach (string file in Directory.EnumerateFiles(FfmpegDir))
                    {
                        string fileName = Path.GetFileName(file);
                        if (fileName != null && fileName.StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase))
                            return file;
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>Runs ffmpeg with the given arguments; returns combined stdout/stderr and exit code.</summary>
        public static async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments)
        {
            string binary = FindBinary();
            if (string.IsNullOrEmpty(binary))
                return (-1, "ffmpeg not found. Place a binary in " + FfmpegDir);

            var cmd = new List<string> { binary };
            if (arguments != null)
                cmd.AddRange(arguments);

            return await Task.Run(() =>
            {
                try
                {
                    using Java.Lang.Process process = Java.Lang.Runtime.GetRuntime().Exec(cmd.ToArray());
                    string stdout = process.InputStream != null
                        ? new StreamReader(process.InputStream).ReadToEnd()
                        : string.Empty;
                    string stderr = process.ErrorStream != null
                        ? new StreamReader(process.ErrorStream).ReadToEnd()
                        : string.Empty;
                    int code = process.WaitFor();
                    var combined = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(stderr))
                        combined.AppendLine(stderr.Trim());
                    if (!string.IsNullOrWhiteSpace(stdout))
                        combined.AppendLine(stdout.Trim());
                    return (code, combined.ToString().Trim());
                }
                catch (Exception ex)
                {
                    return (-1, ex.Message);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>Parses a duration field as total seconds (supports <c>mm:ss</c>, <c>hh:mm:ss</c>, or plain seconds).</summary>
        public static bool TryParseDuration(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out seconds) && seconds >= 0)
                return true;

            string[] parts = text.Split(':');
            if (parts.Length is 2 or 3)
            {
                try
                {
                    double total = 0;
                    if (parts.Length == 3)
                    {
                        if (!int.TryParse(parts[0], out int h) ||
                            !int.TryParse(parts[1], out int m) ||
                            !double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double s))
                            return false;
                        total = h * 3600 + m * 60 + s;
                    }
                    else
                    {
                        if (!int.TryParse(parts[0], out int m) ||
                            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double s))
                            return false;
                        total = m * 60 + s;
                    }

                    seconds = total;
                    return seconds >= 0;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
