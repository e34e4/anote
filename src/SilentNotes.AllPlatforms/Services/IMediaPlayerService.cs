// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Browses device storage and plays audio/video files for the Player tab. Also prepared so
    /// the same tab can later act as a pictures gallery viewer.
    /// </summary>
    public interface IMediaPlayerService
    {
        /// <summary>Gets a value indicating whether media playback is supported on this platform.</summary>
        bool IsSupported { get; }

        /// <summary>Gets a value indicating whether all-files / broad storage access is granted.</summary>
        bool HasAllFilesPermission { get; }

        /// <summary>Requests all-files storage access (opens system settings on Android 11+).</summary>
        Task<bool> RequestAllFilesPermissionAsync();

        /// <summary>Gets the absolute path of the directory currently being browsed.</summary>
        string CurrentDirectory { get; }

        /// <summary>Gets a short display label for <see cref="CurrentDirectory"/>.</summary>
        string CurrentDirectoryDisplay { get; }

        /// <summary>Gets a value indicating whether navigating one level up is possible.</summary>
        bool CanNavigateUp { get; }

        /// <summary>Lists folders and media files in the current directory.</summary>
        IReadOnlyList<MediaItemModel> ListCurrentDirectory();

        /// <summary>Enters a subdirectory.</summary>
        void EnterDirectory(string path);

        /// <summary>Navigates one level up, when possible.</summary>
        void NavigateUp();

        /// <summary>Jumps to a well-known root (internal storage, Music, Movies, DCIM, Downloads).</summary>
        void GoToRoot(string rootKey);

        /// <summary>Gets available storage roots for quick navigation.</summary>
        IReadOnlyList<(string Key, string Label, string Path)> GetStorageRoots();

        /// <summary>Raised when playback state or progress changes.</summary>
        event Action StateChanged;

        /// <summary>Gets the path/URI of the item currently loaded in the player, or null.</summary>
        string ActivePath { get; }

        /// <summary>Gets the display name of the active item.</summary>
        string ActiveTitle { get; }

        /// <summary>Gets the kind of the active item.</summary>
        MediaKind? ActiveKind { get; }

        /// <summary>Gets a value indicating whether media is currently playing.</summary>
        bool IsPlaying { get; }

        /// <summary>Gets a value indicating whether media is loaded but paused.</summary>
        bool IsPaused { get; }

        /// <summary>Gets the current playback position.</summary>
        TimeSpan Position { get; }

        /// <summary>Gets the duration of the loaded media.</summary>
        TimeSpan Duration { get; }

        /// <summary>Opens and plays an audio or video file (videos open a fullscreen player).</summary>
        Task PlayAsync(MediaItemModel item);

        /// <summary>Opens and plays media from a content URI / path (e.g. system VIEW intent).</summary>
        Task PlayUriAsync(string uriOrPath, string mimeType = null, string displayName = null);

        /// <summary>Pauses playback.</summary>
        void Pause();

        /// <summary>Resumes paused playback.</summary>
        void Resume();

        /// <summary>Stops playback and unloads the media.</summary>
        void Stop();

        /// <summary>Seeks to a position (0..1 of duration).</summary>
        void SeekRelative(double relative);

        /// <summary>
        /// Opens an image for viewing. Currently uses a simple fullscreen viewer; the Player tab
        /// is structured so this can grow into a gallery later.
        /// </summary>
        Task OpenImageAsync(MediaItemModel item);

        /// <summary>Lets the user pick a media file via the system picker and plays/opens it.</summary>
        Task PickAndOpenAsync();
    }
}
