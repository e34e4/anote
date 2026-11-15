// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>Persisted player queue.</summary>
    public class PlayerQueueState
    {
        /// <summary>Gets or sets the queued paths.</summary>
        public List<string> Items { get; set; } = new List<string>();

        /// <summary>Gets or sets the index of the current item.</summary>
        public int Index { get; set; }
    }

    /// <summary>
    /// Stores the Player queue in a small JSON file, so a playlist survives an app restart.
    /// </summary>
    public static class PlayerQueueStore
    {
        private static string GetPath()
        {
            return Path.Combine(AnotePaths.GetRootDirectory(), "player-queue.json");
        }

        /// <summary>Loads the persisted queue, or an empty state.</summary>
        public static PlayerQueueState Load()
        {
            try
            {
                string path = GetPath();
                if (!File.Exists(path))
                    return new PlayerQueueState();
                PlayerQueueState state = JsonSerializer.Deserialize<PlayerQueueState>(File.ReadAllText(path));
                if (state?.Items == null)
                    return new PlayerQueueState();
                state.Items.RemoveAll(string.IsNullOrWhiteSpace);
                state.Index = Math.Clamp(state.Index, 0, Math.Max(0, state.Items.Count - 1));
                return state;
            }
            catch (Exception)
            {
                return new PlayerQueueState();
            }
        }

        /// <summary>Saves the queue.</summary>
        public static void Save(PlayerQueueState state)
        {
            try
            {
                string path = GetPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonSerializer.Serialize(state ?? new PlayerQueueState()));
            }
            catch (Exception)
            {
            }
        }
    }
}
