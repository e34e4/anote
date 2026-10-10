using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Services;

namespace SilentNotesTest.Services
{
    [TestClass]
    public class ClipboardHistoryServiceTest
    {
        private string _directory;

        [TestInitialize]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "anote_clipboard_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (Exception)
            {
            }
        }

        private TestableClipboardHistoryService CreateService()
        {
            return new TestableClipboardHistoryService(new XmlFileService(), _directory);
        }

        [TestMethod]
        public void AddTextStoresNewestFirst()
        {
            TestableClipboardHistoryService service = CreateService();
            service.AddText("first", 10);
            service.AddText("second", 10);

            var model = service.LoadHistoryOrDefault();
            Assert.AreEqual(2, model.Items.Count);
            Assert.AreEqual("second", model.Items[0].Text);
            Assert.AreEqual("first", model.Items[1].Text);
        }

        [TestMethod]
        public void AddTextMovesDuplicateToTop()
        {
            TestableClipboardHistoryService service = CreateService();
            service.AddText("a", 10);
            service.AddText("b", 10);
            service.AddText("a", 10);

            var model = service.LoadHistoryOrDefault();
            Assert.AreEqual(2, model.Items.Count);
            Assert.AreEqual("a", model.Items[0].Text);
            Assert.AreEqual("b", model.Items[1].Text);
        }

        [TestMethod]
        public void TrimToLimitRemovesOldestNonFavorites()
        {
            TestableClipboardHistoryService service = CreateService();
            service.AddText("1", 3);
            service.AddText("2", 3);
            service.AddText("3", 3);
            service.AddText("4", 3);

            var model = service.LoadHistoryOrDefault();
            Assert.AreEqual(3, model.Items.Count);
            Assert.IsFalse(model.Items.Any(i => i.Text == "1"));
            Assert.IsTrue(model.Items.Any(i => i.Text == "4"));
        }

        [TestMethod]
        public void TrimToLimitKeepsFavorites()
        {
            TestableClipboardHistoryService service = CreateService();
            service.AddText("fav", 10);
            var favorite = service.LoadHistoryOrDefault().Items[0];
            service.ToggleFavorite(favorite.Id);

            service.AddText("2", 2);
            service.AddText("3", 2);
            service.AddText("4", 2);

            var model = service.LoadHistoryOrDefault();
            // 1 favorite + only 2 non-favorites are kept
            Assert.AreEqual(3, model.Items.Count);
            Assert.IsTrue(model.Items.Any(i => (i.Text == "fav") && i.IsFavorite));
            Assert.IsFalse(model.Items.Any(i => i.Text == "2"));
        }

        [TestMethod]
        public void ClearNonFavoritesKeepsFavorites()
        {
            TestableClipboardHistoryService service = CreateService();
            service.AddText("keep", 10);
            var favorite = service.LoadHistoryOrDefault().Items[0];
            service.ToggleFavorite(favorite.Id);
            service.AddText("drop", 10);

            service.ClearNonFavorites();

            var model = service.LoadHistoryOrDefault();
            Assert.AreEqual(1, model.Items.Count);
            Assert.AreEqual("keep", model.Items[0].Text);
        }

        [TestMethod]
        public void AddAndDeleteImageRemovesFiles()
        {
            TestableClipboardHistoryService service = CreateService();
            byte[] image = new byte[] { 1, 2, 3, 4, 5 };
            var item = service.AddImage(image, image, 10);

            Assert.IsTrue(File.Exists(service.GetFullImagePath(item.ImageFileName)));
            Assert.IsTrue(File.Exists(service.GetThumbImagePath(item.ImageFileName)));

            service.Delete(item.Id);

            Assert.IsFalse(File.Exists(service.GetFullImagePath(item.ImageFileName)));
            Assert.IsFalse(File.Exists(service.GetThumbImagePath(item.ImageFileName)));
        }

        private class TestableClipboardHistoryService : ClipboardHistoryServiceBase
        {
            private readonly string _directory;

            public TestableClipboardHistoryService(IXmlFileService xmlFileService, string directory)
                : base(xmlFileService)
            {
                _directory = directory;
            }

            protected override string GetLocation()
            {
                return _directory;
            }
        }
    }
}
