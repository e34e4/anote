// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using Android.Util;
using SilentNotes;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Saves voice memo dictation + optional recording attachment into the Voice note.
    /// </summary>
    internal static class RecVoiceMemoPersist
    {
        private const string LogTag = "aNoteRecVoice";

        public static bool Save(string transcript, string audioFilePath, string audioFileName)
        {
            try
            {
                IRepositoryStorageService repo = Ioc.Instance.GetService<IRepositoryStorageService>();
                if (repo == null)
                {
                    Log.Warn(LogTag, "Repository service unavailable");
                    return false;
                }

                repo.LoadRepositoryOrDefault(out NoteRepositoryModel model);
                NoteModel voiceNote = NoteVoiceMemoHelper.AppendMemo(model, transcript, audioFileName);

                TryAttachRecording(voiceNote.Id, audioFilePath, audioFileName);

                bool saved = repo.TrySaveRepository(model);
                if (saved)
                {
                    try
                    {
                        Ioc.Instance.GetService<IMessengerService>()?.Send(new VoiceNoteChangedMessage());
                        Ioc.Instance.GetService<IMessengerService>()?.Send(new NotesChangedMessage());
                    }
                    catch (Exception)
                    {
                    }
                }

                return saved;
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, "Save failed: " + ex);
                return false;
            }
        }

        private static void TryAttachRecording(Guid noteId, string audioFilePath, string audioFileName)
        {
            if (noteId == Guid.Empty || string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
                return;

            try
            {
                INoteAttachmentService attachments = Ioc.Instance.GetService<INoteAttachmentService>();
                if (attachments == null)
                    return;

                byte[] content = File.ReadAllBytes(audioFilePath);
                string name = string.IsNullOrWhiteSpace(audioFileName)
                    ? Path.GetFileName(audioFilePath)
                    : audioFileName;
                attachments.TryAddAttachment(noteId, content, name, "audio/mp4", out _, out _);
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, "Attachment skipped: " + ex.Message);
            }
        }
    }
}