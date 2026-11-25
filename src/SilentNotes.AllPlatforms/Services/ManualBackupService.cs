// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Result of a manual backup.
    /// </summary>
    public class ManualBackupResult
    {
        /// <summary>Gets or sets a value indicating whether the backup was created successfully.</summary>
        public bool Success { get; set; }

        /// <summary>Gets or sets the folder which contains the created backup.</summary>
        public string Folder { get; set; }

        /// <summary>Gets or sets the error message if the backup failed.</summary>
        public string Error { get; set; }

        /// <summary>Gets the list of written files (relative to <see cref="Folder"/>).</summary>
        public List<string> Files { get; } = new List<string>();
    }

    /// <summary>
    /// Creates an on-demand backup of the local app data: the note repository, the clipboard
    /// history with its images, the SQLite article cache, and human readable Markdown/JSON
    /// bundles of notes, contacts, bookmarks and clips.
    /// </summary>
    public interface IManualBackupService
    {
        /// <summary>Gets the default folder for backups, e.g. <c>/sdcard/anote/bak</c>.</summary>
        string DefaultFolder { get; }

        /// <summary>
        /// Creates a complete backup in a timestamped sub folder of <paramref name="folder"/>.
        /// </summary>
        /// <param name="folder">The target folder, or null/empty to use <see cref="DefaultFolder"/>.</param>
        /// <returns>The result of the backup.</returns>
        Task<ManualBackupResult> CreateBackupAsync(string folder);
    }

    /// <inheritdoc/>
    public class ManualBackupService : IManualBackupService
    {
        private readonly IRepositoryStorageService _repositoryStorageService;
        private readonly IClipboardHistoryService _clipboardHistoryService;
        private readonly IRssArticleCacheService _rssArticleCacheService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualBackupService"/> class.
        /// </summary>
        public ManualBackupService(
            IRepositoryStorageService repositoryStorageService,
            IClipboardHistoryService clipboardHistoryService,
            IRssArticleCacheService rssArticleCacheService)
        {
            _repositoryStorageService = repositoryStorageService;
            _clipboardHistoryService = clipboardHistoryService;
            _rssArticleCacheService = rssArticleCacheService;
        }

        /// <inheritdoc/>
        public string DefaultFolder
        {
            get { return Path.Combine(AnotePaths.GetRootDirectory(), "bak"); }
        }

        /// <inheritdoc/>
        public Task<ManualBackupResult> CreateBackupAsync(string folder)
        {
            return Task.Run(() => CreateBackup(folder));
        }

        private ManualBackupResult CreateBackup(string folder)
        {
            var result = new ManualBackupResult();
            try
            {
                string rootFolder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder.Trim();
                string backupFolder = Path.Combine(rootFolder, "anote-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backupFolder);
                result.Folder = backupFolder;

                RepositoryStorageLoadResult loadResult = _repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
                if (loadResult == RepositoryStorageLoadResult.InvalidRepository)
                {
                    result.Error = "The note repository could not be loaded.";
                    return result;
                }

                CopyRawFiles(backupFolder, result);
                WriteNoteBundles(backupFolder, repository, result);
                WriteClipboardBundle(backupFolder, result);
                WriteContactAndBookmarkBundles(backupFolder, repository, result);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }
            return result;
        }

        private void CopyRawFiles(string backupFolder, ManualBackupResult result)
        {
            string rawFolder = Path.Combine(backupFolder, "raw");
            Directory.CreateDirectory(rawFolder);

            // Note repository (the main data file).
            byte[] repositoryBytes = _repositoryStorageService.LoadRepositoryFile();
            if (repositoryBytes != null && repositoryBytes.Length > 0)
            {
                WriteFile(Path.Combine(rawFolder, NoteRepositoryModel.RepositoryFileName), repositoryBytes);
                result.Files.Add("raw/" + NoteRepositoryModel.RepositoryFileName);
            }

            // Clipboard history and its images.
            string historyFile = _clipboardHistoryService.GetHistoryFilePath();
            if (File.Exists(historyFile))
            {
                WriteFile(Path.Combine(rawFolder, Path.GetFileName(historyFile)), File.ReadAllBytes(historyFile));
                result.Files.Add("raw/" + Path.GetFileName(historyFile));
            }

            string imageFolder = _clipboardHistoryService.GetImagesDirectoryPath();
            if (Directory.Exists(imageFolder))
            {
                string imageTarget = Path.Combine(rawFolder, "clipboard_images");
                Directory.CreateDirectory(imageTarget);
                foreach (string imageFile in Directory.GetFiles(imageFolder))
                {
                    WriteFile(Path.Combine(imageTarget, Path.GetFileName(imageFile)), File.ReadAllBytes(imageFile));
                    result.Files.Add("raw/clipboard_images/" + Path.GetFileName(imageFile));
                }
            }

            // SQLite article cache (including its journal files when present).
            string databaseFile = _rssArticleCacheService.GetDatabaseFilePath();
            foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                string source = databaseFile + suffix;
                if (!File.Exists(source))
                    continue;
                string name = Path.GetFileName(source);
                WriteFile(Path.Combine(rawFolder, name), File.ReadAllBytes(source));
                result.Files.Add("raw/" + name);
            }
        }

        private void WriteNoteBundles(string backupFolder, NoteRepositoryModel repository, ManualBackupResult result)
        {
            List<NoteModel> notes = repository.Notes
                .Where(note => !note.InRecyclingBin)
                .OrderByDescending(note => note.ModifiedAt)
                .ToList();

            string notesFolder = Path.Combine(backupFolder, "notes");
            Directory.CreateDirectory(notesFolder);

            var markdown = new StringBuilder();
            markdown.Append("# aNote backup ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine();
            markdown.AppendLine();

            using (var jsonStream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(jsonStream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteString("exportedAt", DateTime.UtcNow.ToString("o"));
                    writer.WriteString("repositoryId", repository.Id.ToString());
                    writer.WriteNumber("noteCount", notes.Count);
                    writer.WriteStartArray("notes");
                    foreach (NoteModel note in notes)
                    {
                        bool locked = note.SafeId.HasValue && note.SafeId.Value != Guid.Empty;
                        string title = locked ? "Encrypted note" : NoteHtmlUtils.GetTitle(note.HtmlContent);
                        if (string.IsNullOrWhiteSpace(title))
                            title = "Untitled note";

                        writer.WriteStartObject();
                        writer.WriteString("id", note.Id.ToString());
                        writer.WriteString("title", title);
                        writer.WriteString("noteType", note.NoteType.ToString());
                        writer.WriteString("createdAt", note.CreatedAt.ToString("o"));
                        writer.WriteString("modifiedAt", note.ModifiedAt.ToString("o"));
                        writer.WriteBoolean("pinned", note.IsPinned);
                        writer.WriteBoolean("locked", locked);
                        writer.WriteStartArray("tags");
                        foreach (string tag in note.Tags ?? new List<string>())
                            writer.WriteStringValue(tag);
                        writer.WriteEndArray();
                        if (!locked)
                        {
                            writer.WriteString("html", note.HtmlContent);
                            writer.WriteString("markdown", SimpleHtmlToMarkdown.Convert(note.HtmlContent));
                        }
                        writer.WriteEndObject();

                        markdown.Append("## ").Append(title).AppendLine();
                        markdown.Append("_").Append(note.ModifiedAt.ToString("yyyy-MM-dd HH:mm")).Append("_");
                        if (note.Tags != null && note.Tags.Count > 0)
                            markdown.Append(" · ").Append(string.Join(", ", note.Tags));
                        markdown.AppendLine().AppendLine();
                        if (locked)
                        {
                            markdown.AppendLine("> This note is protected by a safe, its content is only available in the encrypted repository.");
                        }
                        else
                        {
                            markdown.AppendLine(SimpleHtmlToMarkdown.Convert(note.HtmlContent));
                            WriteFile(
                                Path.Combine(notesFolder, NoteHtmlUtils.Slugify(title) + "-" + note.Id.ToString("N") + ".md"),
                                Encoding.UTF8.GetBytes(SimpleHtmlToMarkdown.Convert(note.HtmlContent)));
                        }
                        markdown.AppendLine();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                WriteFile(Path.Combine(backupFolder, "notes.json"), jsonStream.ToArray());
                result.Files.Add("notes.json");
            }

            WriteFile(Path.Combine(backupFolder, "notes.md"), Encoding.UTF8.GetBytes(markdown.ToString()));
            result.Files.Add("notes.md");
            result.Files.Add("notes/");
        }

        private void WriteClipboardBundle(string backupFolder, ManualBackupResult result)
        {
            ClipboardHistoryModel history = _clipboardHistoryService.LoadHistoryOrDefault();
            List<ClipboardItemModel> items = history.Items
                .Where(item => !item.IsDeleted)
                .OrderByDescending(item => item.CreatedUtc)
                .ToList();

            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteString("exportedAt", DateTime.UtcNow.ToString("o"));
                    writer.WriteNumber("itemCount", items.Count);
                    writer.WriteStartArray("items");
                    foreach (ClipboardItemModel item in items)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", item.Id.ToString());
                        writer.WriteString("createdAt", item.CreatedUtc.ToString("o"));
                        writer.WriteBoolean("favorite", item.IsFavorite);
                        writer.WriteString("kind", item.Kind.ToString());
                        if (item.Kind == ClipboardItemKind.Text)
                            writer.WriteString("text", item.Text);
                        else
                            writer.WriteString("imageFileName", item.ImageFileName);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                WriteFile(Path.Combine(backupFolder, "clipboard.json"), stream.ToArray());
                result.Files.Add("clipboard.json");
            }
        }

        private void WriteContactAndBookmarkBundles(string backupFolder, NoteRepositoryModel repository, ManualBackupResult result)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteStartArray("contacts");
                    foreach (ContactModel contact in repository.Contacts)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", contact.Id.ToString());
                        writer.WriteString("name", contact.Name);
                        writer.WriteStartArray("phones");
                        foreach (string phone in contact.Phones)
                            writer.WriteStringValue(phone);
                        writer.WriteEndArray();
                        if (!string.IsNullOrEmpty(contact.Notes))
                            writer.WriteString("notes", contact.Notes);
                        if (contact.FollowUpIntervalDays != 0)
                            writer.WriteNumber("followUpIntervalDays", contact.FollowUpIntervalDays);
                        if (contact.LastInteractionUtc.HasValue)
                            writer.WriteString("lastInteractionUtc", contact.LastInteractionUtc.Value.ToString("o"));
                        if (contact.NextFollowUpUtc.HasValue)
                            writer.WriteString("nextFollowUpUtc", contact.NextFollowUpUtc.Value.ToString("o"));
                        if (contact.InteractionLog.Count > 0)
                        {
                            writer.WriteStartArray("interactionLog");
                            foreach (ContactInteractionEntryModel entry in contact.InteractionLog)
                            {
                                writer.WriteStartObject();
                                writer.WriteString("utc", entry.Utc.ToString("o"));
                                writer.WriteString("summary", entry.Summary);
                                writer.WriteEndObject();
                            }
                            writer.WriteEndArray();
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                WriteFile(Path.Combine(backupFolder, "contacts.json"), stream.ToArray());
                result.Files.Add("contacts.json");
            }

            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteStartArray("bookmarks");
                    foreach (BookmarkModel bookmark in repository.Bookmarks)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", bookmark.Id.ToString());
                        writer.WriteString("title", bookmark.Title);
                        writer.WriteString("url", bookmark.Url);
                        writer.WriteString("quote", bookmark.Quote);
                        writer.WriteString("note", bookmark.Note);
                        writer.WriteString("modifiedAt", bookmark.ModifiedAt.ToString("o"));
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                WriteFile(Path.Combine(backupFolder, "bookmarks.json"), stream.ToArray());
                result.Files.Add("bookmarks.json");
            }
        }

        private static void WriteFile(string path, byte[] content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, content);
        }
    }
}
