// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class NoteLinkExpanderTest
    {
        [TestMethod]
        public void ExpandsNoteLinkByTitle()
        {
            NoteRepositoryModel repository = CreateRepository();
            NoteModel target = AddNote(repository, "Holiday plans");

            string result = NoteLinkExpander.Expand("<p>See [[Holiday plans]] for details.</p>", repository);

            StringAssert.Contains(result, "href=\"anote://note/" + target.Id.ToString("N") + "\"");
            StringAssert.Contains(result, ">Holiday plans</a>");
            Assert.IsFalse(result.Contains("[["));
        }

        [TestMethod]
        public void ExpandsContactAndBookmarkLinks()
        {
            NoteRepositoryModel repository = CreateRepository();
            var contact = new ContactModel { Name = "Alice" };
            contact.Phones.Add("+41 79 123 45 67");
            repository.Contacts.Add(contact);

            var bookmark = new BookmarkModel { Title = "Docs", Url = "https://example.com/docs" };
            repository.Bookmarks.Add(bookmark);

            string result = NoteLinkExpander.Expand("<p>Write to @Alice, read [[bookmark:Docs]].</p>", repository);

            StringAssert.Contains(result, "href=\"tel:+41791234567\"");
            StringAssert.Contains(result, "href=\"anote://bookmark/" + bookmark.Id.ToString("N") + "\"");
        }

        [TestMethod]
        public void LeavesUnknownTitlesUnchanged()
        {
            NoteRepositoryModel repository = CreateRepository();

            string result = NoteLinkExpander.Expand("<p>See [[Missing note]] and @Nobody</p>", repository);

            StringAssert.Contains(result, "[[Missing note]]");
            StringAssert.Contains(result, "@Nobody");
        }

        [TestMethod]
        public void DoesNotChangeExistingLinksCodeOrAttributes()
        {
            NoteRepositoryModel repository = CreateRepository();
            AddNote(repository, "Target");

            string html = "<p><a href=\"https://x/[[Target]]\">[[Target]]</a> <code>[[Target]]</code></p>";

            string result = NoteLinkExpander.Expand(html, repository);

            Assert.AreEqual(html, result);
        }

        [TestMethod]
        public void ExpansionIsIdempotent()
        {
            NoteRepositoryModel repository = CreateRepository();
            AddNote(repository, "Target");

            string once = NoteLinkExpander.Expand("<p>[[Target]]</p>", repository);
            string twice = NoteLinkExpander.Expand(once, repository);

            Assert.AreEqual(once, twice);
        }

        [TestMethod]
        public void ParsesCustomUris()
        {
            Guid noteId = Guid.NewGuid();
            Guid bookmarkId = Guid.NewGuid();

            Assert.IsTrue(NoteLinkExpander.TryParseCustomUri(NoteLinkExpander.BuildNoteLink(noteId), out string target, out Guid id));
            Assert.AreEqual("note", target);
            Assert.AreEqual(noteId, id);

            Assert.IsTrue(NoteLinkExpander.TryParseCustomUri(NoteLinkExpander.BuildBookmarkLink(bookmarkId), out target, out id));
            Assert.AreEqual("bookmark", target);
            Assert.AreEqual(bookmarkId, id);

            Assert.IsFalse(NoteLinkExpander.TryParseCustomUri("https://example.com", out _, out _));
            Assert.IsFalse(NoteLinkExpander.TryParseCustomUri("anote://note/not-a-guid", out _, out _));
        }

        private static NoteRepositoryModel CreateRepository()
        {
            return new NoteRepositoryModel { Revision = NoteRepositoryModel.CurrentSavingRevision };
        }

        private static NoteModel AddNote(NoteRepositoryModel repository, string title)
        {
            var note = new NoteModel { HtmlContent = "<h1>" + title + "</h1><p>Body</p>" };
            repository.Notes.Add(note);
            return note;
        }
    }
}
