// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Shared ordering for file browser lists: directories first, then files, per <see cref="FileBrowserSortMode"/>.
    /// </summary>
    public static class FileBrowserSort
    {
        public static List<T> Apply<T>(
            IEnumerable<T> items,
            FileBrowserSortMode mode,
            Func<T, bool> isDirectory,
            Func<T, string> getName,
            Func<T, DateTime> getModified,
            Func<T, long> getSize)
        {
            if (items == null)
                return new List<T>();

            List<T> list = items.ToList();
            List<T> directories = SortGroup(list.Where(isDirectory), mode, getName, getModified, getSize);
            List<T> files = SortGroup(list.Where(i => !isDirectory(i)), mode, getName, getModified, getSize);
            directories.AddRange(files);
            return directories;
        }

        public static FileBrowserSortMode ParseOrDefault(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return FileBrowserSortMode.DateDesc;
            return Enum.TryParse(value, true, out FileBrowserSortMode mode)
                ? mode
                : FileBrowserSortMode.DateDesc;
        }

        private static List<T> SortGroup<T>(
            IEnumerable<T> items,
            FileBrowserSortMode mode,
            Func<T, string> getName,
            Func<T, DateTime> getModified,
            Func<T, long> getSize)
        {
            IEnumerable<T> query = items;
            switch (mode)
            {
                case FileBrowserSortMode.NameAsc:
                    query = query.OrderBy(getName, StringComparer.OrdinalIgnoreCase);
                    break;
                case FileBrowserSortMode.SizeDesc:
                    query = query
                        .OrderByDescending(getSize)
                        .ThenBy(getName, StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    query = query
                        .OrderByDescending(getModified)
                        .ThenBy(getName, StringComparer.OrdinalIgnoreCase);
                    break;
            }

            return query.ToList();
        }
    }
}