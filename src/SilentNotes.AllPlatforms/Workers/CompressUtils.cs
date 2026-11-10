// Copyright © 2018 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Helper class for compression and decompression of data with the GZip algorithm,
    /// zip archives, and binary file splitting/joining.
    /// </summary>
    public static class CompressUtils
    {
        /// <summary>
        /// Compresses data with GZip. Don't use this function repeatedly on already compressed data.
        /// </summary>
        /// <param name="data">Data to compress.</param>
        /// <returns>Compressed data, or null if <paramref name="data"/> was null.</returns>
        public static byte[] Compress(byte[] data)
        {
            if (data == null)
                return null;
            if (data.Length == 0)
                return new byte[0];

            byte[] result;
            using (MemoryStream inputStream = new MemoryStream(data))
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (GZipStream zipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
                {
                    inputStream.CopyTo(zipStream);
                }
                result = outputStream.ToArray();
            }
            return result;
        }

        /// <summary>
        /// Decompresses data previously compressed with <see cref="Compress(byte[])"/>.
        /// </summary>
        /// <param name="compressedData">Compressed data to decompress.</param>
        /// <returns>Decompressed data, or null if <paramref name="compressedData"/> was null.</returns>
        public static byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null)
                return null;
            if (compressedData.Length == 0)
                return new byte[0];

            byte[] result;
            using (MemoryStream inputStream = new MemoryStream(compressedData))
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (GZipStream zipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                {
                    zipStream.CopyTo(outputStream);
                }
                result = outputStream.ToArray();
            }
            return result;
        }

        /// <summary>
        /// Creates a zip archive from a list of files.
        /// Entry names may include relative path segments (e.g. "folder/file.txt").
        /// </summary>
        /// <param name="entries">List of files, containing the filename and the file content.</param>
        /// <returns>The content of the zip archive.</returns>
        public static byte[] CreateZipArchive(IEnumerable<CompressEntry> entries)
        {
            byte[] result;
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (var zipArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, true))
                {
                    foreach (CompressEntry inputEntry in entries)
                    {
                        if (inputEntry == null || string.IsNullOrWhiteSpace(inputEntry.Name))
                            continue;

                        string entryName = NormalizeZipEntryName(inputEntry.Name);
                        var archiveEntry = zipArchive.CreateEntry(entryName);
                        using (Stream entryOutputStream = archiveEntry.Open())
                        {
                            byte[] data = inputEntry.Data ?? Array.Empty<byte>();
                            if (data.Length > 0)
                                entryOutputStream.Write(data, 0, data.Length);
                        }
                    }
                }
                result = outputStream.ToArray();
            }
            return result;
        }

        /// <summary>
        /// Opens a zip archive and returns a list of files.
        /// Directory entries (trailing slash / empty name) are skipped.
        /// </summary>
        /// <param name="zipContent">The content of a zip archive.</param>
        /// <returns>List of files, containing the filename and the file content.</returns>
        public static List<CompressEntry> OpenZipArchive(byte[] zipContent)
        {
            var result = new List<CompressEntry>();
            if (zipContent == null || zipContent.Length == 0)
                return result;

            using (MemoryStream inputArchiveStream = new MemoryStream(zipContent))
            using (ZipArchive zipArchive = new ZipArchive(inputArchiveStream, ZipArchiveMode.Read))
            {
                foreach (var archiveEntry in zipArchive.Entries)
                {
                    string name = archiveEntry.FullName ?? archiveEntry.Name;
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.EndsWith("/", StringComparison.Ordinal) ||
                        name.EndsWith("\\", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using (var entryInputStream = archiveEntry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        entryInputStream.CopyTo(buffer);
                        result.Add(new CompressEntry
                        {
                            Name = name.Replace('\\', '/'),
                            Data = buffer.ToArray()
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Splits a binary blob into fixed-size chunks named with a part suffix
        /// (e.g. file.bin.part001, file.bin.part002).
        /// </summary>
        /// <param name="data">File content to split.</param>
        /// <param name="baseFileName">Original file name used as the part name prefix.</param>
        /// <param name="chunkSizeBytes">Maximum size of each part in bytes (must be &gt; 0).</param>
        /// <returns>Ordered list of part entries.</returns>
        public static List<CompressEntry> SplitFile(byte[] data, string baseFileName, int chunkSizeBytes)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (chunkSizeBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes));

            string safeName = string.IsNullOrWhiteSpace(baseFileName) ? "file.bin" : Path.GetFileName(baseFileName);
            var result = new List<CompressEntry>();
            if (data.Length == 0)
            {
                result.Add(new CompressEntry
                {
                    Name = safeName + ".part001",
                    Data = Array.Empty<byte>(),
                });
                return result;
            }

            int partIndex = 1;
            int offset = 0;
            while (offset < data.Length)
            {
                int length = Math.Min(chunkSizeBytes, data.Length - offset);
                byte[] chunk = new byte[length];
                Buffer.BlockCopy(data, offset, chunk, 0, length);
                result.Add(new CompressEntry
                {
                    Name = string.Format(CultureInfo.InvariantCulture, "{0}.part{1:000}", safeName, partIndex),
                    Data = chunk,
                });
                offset += length;
                partIndex++;
            }
            return result;
        }

        /// <summary>
        /// Joins previously split parts back into a single file. Parts are ordered by name.
        /// </summary>
        /// <param name="parts">Part entries (typically from <see cref="SplitFile"/>).</param>
        /// <returns>Concatenated binary content.</returns>
        public static byte[] JoinFiles(IEnumerable<CompressEntry> parts)
        {
            if (parts == null)
                return Array.Empty<byte>();

            List<CompressEntry> ordered = parts
                .Where(p => p != null && p.Data != null)
                .OrderBy(p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using (MemoryStream output = new MemoryStream())
            {
                foreach (CompressEntry part in ordered)
                {
                    if (part.Data.Length > 0)
                        output.Write(part.Data, 0, part.Data.Length);
                }
                return output.ToArray();
            }
        }

        private static string NormalizeZipEntryName(string name)
        {
            string normalized = (name ?? string.Empty).Replace('\\', '/').Trim();
            while (normalized.StartsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(1);
            return string.IsNullOrEmpty(normalized) ? "file.bin" : normalized;
        }

        public class CompressEntry
        {
            public string Name { get; set; }

            public byte[] Data { get; set; }
        }
    }
}
