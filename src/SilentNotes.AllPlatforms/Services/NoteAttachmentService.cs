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
using SilentNotes.Crypto;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <inheritdoc/>
    public class NoteAttachmentService : INoteAttachmentService
    {
        private const string AttachmentFolderName = "attachments";
        private const string IndexFileName = "index.json";
        private const string SafeKeyPrefix = "s:";
        private const string DeviceKeyPrefix = "d:";
        private const int AttachmentKeySize = 32;

        private readonly IRepositoryStorageService _repositoryService;
        private readonly ISafeKeyService _safeKeyService;
        private readonly IDataProtectionService _dataProtectionService;
        private readonly ICryptoRandomService _randomService;
        private readonly ISettingsService _settingsService;
        private readonly object _syncRoot = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="NoteAttachmentService"/> class.
        /// </summary>
        public NoteAttachmentService(
            IRepositoryStorageService repositoryService,
            ISafeKeyService safeKeyService,
            IDataProtectionService dataProtectionService,
            ICryptoRandomService randomService,
            ISettingsService settingsService)
        {
            _repositoryService = repositoryService;
            _safeKeyService = safeKeyService;
            _dataProtectionService = dataProtectionService;
            _randomService = randomService;
            _settingsService = settingsService;
        }

        /// <inheritdoc/>
        public IReadOnlyList<NoteAttachmentModel> ListAttachments(Guid noteId)
        {
            lock (_syncRoot)
            {
                return LoadIndex(noteId);
            }
        }

        /// <inheritdoc/>
        public bool TryAddAttachment(Guid noteId, byte[] content, string fileName, string mimeType, out NoteAttachmentModel attachment, out string error)
        {
            attachment = null;
            error = null;
            if (noteId == Guid.Empty || content == null || content.Length == 0)
            {
                error = "empty";
                return false;
            }

            try
            {
                lock (_syncRoot)
                {
                    byte[] key = GetOrCreateAttachmentKey(noteId);
                    if (key == null)
                    {
                        error = "locked";
                        return false;
                    }

                    var model = new NoteAttachmentModel
                    {
                        Id = Guid.NewGuid(),
                        FileName = SanitizeFileName(fileName),
                        MimeType = string.IsNullOrWhiteSpace(mimeType) ? GuessMimeType(fileName) : mimeType,
                        SizeBytes = content.Length,
                        CreatedAt = DateTime.UtcNow,
                    };

                    byte[] cipher = CreateCryptor().Encrypt(content, key, NoteModel.CryptorPackageName);
                    string directory = GetAttachmentDirectory(noteId);
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(GetContentPath(noteId, model.Id), cipher);

                    List<NoteAttachmentModel> index = LoadIndex(noteId).ToList();
                    index.Add(model);
                    SaveIndex(noteId, index);

                    attachment = model;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <inheritdoc/>
        public Task<byte[]> ReadAttachmentAsync(Guid noteId, Guid attachmentId)
        {
            try
            {
                lock (_syncRoot)
                {
                    string path = GetContentPath(noteId, attachmentId);
                    if (!File.Exists(path))
                        return Task.FromResult<byte[]>(null);

                    byte[] key = GetExistingAttachmentKey(noteId);
                    if (key == null)
                        return Task.FromResult<byte[]>(null);

                    byte[] cipher = File.ReadAllBytes(path);
                    return Task.FromResult(CreateCryptor().Decrypt(cipher, key));
                }
            }
            catch (Exception)
            {
                return Task.FromResult<byte[]>(null);
            }
        }

        /// <inheritdoc/>
        public bool DeleteAttachment(Guid noteId, Guid attachmentId)
        {
            try
            {
                lock (_syncRoot)
                {
                    string path = GetContentPath(noteId, attachmentId);
                    if (File.Exists(path))
                        File.Delete(path);

                    List<NoteAttachmentModel> index = LoadIndex(noteId).ToList();
                    index.RemoveAll(item => item.Id == attachmentId);
                    SaveIndex(noteId, index);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public void DeleteAllAttachments(Guid noteId)
        {
            try
            {
                lock (_syncRoot)
                {
                    string directory = GetAttachmentDirectory(noteId);
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc/>
        public string BuildAttachmentUri(Guid noteId, Guid attachmentId)
        {
            return NoteAttachmentLink.Build(noteId, attachmentId);
        }

        /// <inheritdoc/>
        public string GuessMimeType(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            switch (extension)
            {
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain";
                case ".md": return "text/markdown";
                case ".json": return "application/json";
                case ".xml": return "application/xml";
                case ".csv": return "text/csv";
                case ".zip": return "application/zip";
                case ".7z": return "application/x-7z-compressed";
                case ".rar": return "application/vnd.rar";
                case ".tar": return "application/x-tar";
                case ".gz": return "application/gzip";
                case ".mp3": return "audio/mpeg";
                case ".m4a": return "audio/mp4";
                case ".wav": return "audio/wav";
                case ".ogg": return "audio/ogg";
                case ".opus": return "audio/opus";
                case ".flac": return "audio/flac";
                case ".mp4": return "video/mp4";
                case ".mkv": return "video/x-matroska";
                case ".webm": return "video/webm";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".svg": return "image/svg+xml";
                case ".doc": return "application/msword";
                case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case ".xls": return "application/vnd.ms-excel";
                case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".ppt": return "application/vnd.ms-powerpoint";
                case ".pptx": return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                case ".epub": return "application/epub+zip";
                default: return "application/octet-stream";
            }
        }

        private byte[] GetOrCreateAttachmentKey(Guid noteId)
        {
            _repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            NoteModel note = repository?.Notes?.FindById(noteId);
            if (note == null)
                return null;

            byte[] key = TryDecodeAttachmentKey(note);
            if (key != null)
                return key;

            // A locked note cannot get a new key until its safe is open.
            bool isLocked = note.SafeId.HasValue && note.SafeId.Value != Guid.Empty
                && !_safeKeyService.IsSafeOpen(note.SafeId.Value);
            if (isLocked)
                return null;

            key = _randomService.GetRandomBytes(AttachmentKeySize);

            if (note.SafeId.HasValue && _safeKeyService.TryGetKey(note.SafeId, out byte[] safeKey))
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                byte[] protectedKey = CreateCryptor().Encrypt(key, safeKey, settings.SelectedEncryptionAlgorithm);
                note.AttachementKey = SafeKeyPrefix + Convert.ToBase64String(protectedKey);
            }
            else
            {
                // Unprotected notes are only protected by the Android keystore/Windows DPAPI.
                note.AttachementKey = DeviceKeyPrefix + _dataProtectionService.Protect(key);
            }

            _repositoryService.TrySaveRepository(repository);
            return key;
        }

        private byte[] GetExistingAttachmentKey(Guid noteId)
        {
            _repositoryService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            NoteModel note = repository?.Notes?.FindById(noteId);
            return note == null ? null : TryDecodeAttachmentKey(note);
        }

        private byte[] TryDecodeAttachmentKey(NoteModel note)
        {
            string stored = note?.AttachementKey;
            if (string.IsNullOrWhiteSpace(stored))
                return null;

            try
            {
                if (stored.StartsWith(SafeKeyPrefix, StringComparison.Ordinal))
                {
                    if (!note.SafeId.HasValue || !_safeKeyService.TryGetKey(note.SafeId, out byte[] safeKey))
                        return null;
                    byte[] protectedKey = Convert.FromBase64String(stored.Substring(SafeKeyPrefix.Length));
                    return CreateCryptor().Decrypt(protectedKey, safeKey);
                }

                if (stored.StartsWith(DeviceKeyPrefix, StringComparison.Ordinal))
                {
                    return _dataProtectionService.Unprotect(stored.Substring(DeviceKeyPrefix.Length));
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private List<NoteAttachmentModel> LoadIndex(Guid noteId)
        {
            string path = Path.Combine(GetAttachmentDirectory(noteId), IndexFileName);
            try
            {
                if (!File.Exists(path))
                    return new List<NoteAttachmentModel>();
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<NoteAttachmentModel>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<NoteAttachmentModel>();
            }
            catch (Exception)
            {
                return new List<NoteAttachmentModel>();
            }
        }

        private void SaveIndex(Guid noteId, List<NoteAttachmentModel> index)
        {
            string directory = GetAttachmentDirectory(noteId);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, IndexFileName);
            string tempPath = path + ".tmp";
            string json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, path, true);
        }

        private string GetAttachmentDirectory(Guid noteId)
        {
            return Path.Combine(_repositoryService.GetLocation(), AttachmentFolderName, noteId.ToString("N"));
        }

        private string GetContentPath(Guid noteId, Guid attachmentId)
        {
            return Path.Combine(GetAttachmentDirectory(noteId), attachmentId.ToString("N") + ".enc");
        }

        private static string SanitizeFileName(string fileName)
        {
            string name = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return "attachment";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        private ICryptor CreateCryptor()
        {
            return new Cryptor(NoteModel.CryptorPackageName, _randomService);
        }
    }
}
