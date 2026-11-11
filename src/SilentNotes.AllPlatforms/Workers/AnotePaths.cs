// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Central paths under the shared <c>anote</c> storage root (Android: <c>/sdcard/anote</c>).
    /// Release builds may place ffmpeg / yt-dlp binaries in <see cref="FfmpegDir"/> and <see cref="YtDlpDir"/>.
    /// </summary>
    public static class AnotePaths
    {
        public const string RootFolderName = "anote";

        private static string _rootDirectory;

        /// <summary>Resolved once; prefers writable external storage, then app-local fallback.</summary>
        public static string GetRootDirectory()
        {
            if (!string.IsNullOrEmpty(_rootDirectory))
                return _rootDirectory;

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
                local = Path.GetTempPath();
            string appLocalRoot = Path.Combine(local, RootFolderName);

            foreach (string candidate in new[]
                     {
                         Path.Combine("/sdcard", RootFolderName),
                         Path.Combine("/storage", "emulated", "0", RootFolderName),
                         appLocalRoot,
                     })
            {
                if (IsWritableDirectory(candidate))
                {
                    _rootDirectory = candidate;
                    return _rootDirectory;
                }
            }

            try
            {
                Directory.CreateDirectory(appLocalRoot);
            }
            catch (Exception)
            {
            }

            _rootDirectory = appLocalRoot;
            return _rootDirectory;
        }

        public static string Down => Path.Combine(GetRootDirectory(), "down");

        public static string Pic => Path.Combine(GetRootDirectory(), "pic");

        public static string Tg => Path.Combine(GetRootDirectory(), "tg");

        public static string Abro => Path.Combine(GetRootDirectory(), "abro");

        /// <summary>File-tab cloud OAuth / staging workspace (<c>/sdcard/anote/file</c>).</summary>
        public static string File => Path.Combine(GetRootDirectory(), "file");

        public static string FfmpegDir => Path.Combine(GetRootDirectory(), "ffmpeg");

        public static string YtDlpDir => Path.Combine(GetRootDirectory(), "yt-dlp");

        /// <summary>Creates standard anote subfolders if missing; returns <see cref="GetRootDirectory"/>.</summary>
        public static string EnsureDirectories()
        {
            string root = GetRootDirectory();
            foreach (string dir in new[] { Down, Pic, Tg, Abro, File, FfmpegDir, YtDlpDir })
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception)
                {
                }
            }

            return root;
        }

        /// <summary>Finds ffmpeg or libffmpeg under <see cref="FfmpegDir"/>, or null.</summary>
        public static string ResolveFfmpegBinary()
        {
            return ResolveBinaryInDir(FfmpegDir, "ffmpeg", "ffmpeg.exe", "libffmpeg.so", "libffmpeg");
        }

        /// <summary>Finds yt-dlp under <see cref="YtDlpDir"/>, or null.</summary>
        public static string ResolveYtDlpBinary()
        {
            return ResolveBinaryInDir(YtDlpDir, "yt-dlp", "yt-dlp.exe");
        }

        private static string ResolveBinaryInDir(string directory, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return null;

            foreach (string name in names)
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path))
                    return path;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    string fileName = Path.GetFileName(file);
                    foreach (string name in names)
                    {
                        if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase)
                            || fileName.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                            return file;
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static bool IsWritableDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            try
            {
                Directory.CreateDirectory(path);
                string probe = Path.Combine(path, ".probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
