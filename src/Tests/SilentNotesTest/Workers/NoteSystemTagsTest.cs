using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class NoteSystemTagsTest
    {
        [TestMethod]
        public void IsSystemTag_RecognizesInternalPrefix()
        {
            Assert.IsTrue(NoteSystemTags.IsSystemTag("__anote_widget__"));
            Assert.IsTrue(NoteSystemTags.IsSystemTag("__anote_voice__"));
            Assert.IsFalse(NoteSystemTags.IsSystemTag("work"));
            Assert.IsFalse(NoteSystemTags.IsSystemTag(null));
        }

        [TestMethod]
        public void WithoutSystemTags_StripsInternalTags()
        {
            List<string> cleaned = NoteSystemTags.WithoutSystemTags(new[] { "work", "__anote_widget__", "home" });
            CollectionAssert.AreEqual(new[] { "work", "home" }, cleaned);
        }

        [TestMethod]
        public void IsOrdinaryNote_RequiresNoSystemTag()
        {
            Assert.IsTrue(NoteSystemTags.IsOrdinaryNote(new[] { "work" }));
            Assert.IsTrue(NoteSystemTags.IsOrdinaryNote(new List<string>()));
            Assert.IsFalse(NoteSystemTags.IsOrdinaryNote(new[] { "__anote_widget__" }));
            Assert.IsFalse(NoteSystemTags.IsOrdinaryNote(new[] { "work", "__anote_daily__" }));
        }

        [TestMethod]
        public void NoteFilter_EmptyTagsAfterStrippingSystemTags_MatchesAll()
        {
            var filter = new NoteFilter(
                null,
                NoteSystemTags.WithoutSystemTags(new[] { "__anote_widget__" }),
                NoteFilter.FilterOptions.FilterByTagList);

            Assert.IsTrue(filter.MatchTags(new List<string> { "work" }));
            Assert.IsTrue(filter.MatchTags(new List<string>()));
            Assert.IsTrue(filter.MatchTags(new List<string> { "__anote_widget__" }));
        }
    }
}
