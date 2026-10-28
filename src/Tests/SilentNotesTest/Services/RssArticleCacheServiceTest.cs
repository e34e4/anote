using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotesTest.Services
{
    [TestClass]
    public class RssArticleCacheServiceTest
    {
        private string _directory;

        [TestInitialize]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "anote_rss_cache_test_" + Guid.NewGuid().ToString("N"));
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

        private TestableRssArticleCacheService CreateService()
        {
            return new TestableRssArticleCacheService(_directory);
        }

        [TestMethod]
        public void MergeInsertsNewArticles()
        {
            TestableRssArticleCacheService service = CreateService();
            Guid feedId = Guid.NewGuid();
            var articles = new List<RssCachedArticleModel>
            {
                CreateArticle(feedId, "g:1", "One", DateTime.UtcNow.AddHours(-2)),
                CreateArticle(feedId, "g:2", "Two", DateTime.UtcNow.AddHours(-1)),
            };

            int merged = service.MergeArticles(feedId, articles);
            Assert.AreEqual(2, merged);

            IReadOnlyList<RssCachedArticleModel> loaded = service.LoadArticles(new[] { feedId });
            Assert.AreEqual(2, loaded.Count);
            Assert.AreEqual("Two", loaded[0].Title);
            Assert.AreEqual("One", loaded[1].Title);
        }

        [TestMethod]
        public void MergeSkipsDuplicateItemKey()
        {
            TestableRssArticleCacheService service = CreateService();
            Guid feedId = Guid.NewGuid();
            DateTime published = DateTime.UtcNow.AddHours(-1);
            service.MergeArticles(feedId, new[]
            {
                CreateArticle(feedId, "g:same", "Original", published, "body-a"),
            });

            int merged = service.MergeArticles(feedId, new[]
            {
                CreateArticle(feedId, "g:same", "Original", published, "body-a"),
            });

            Assert.AreEqual(0, merged);
            Assert.AreEqual(1, service.LoadArticles(new[] { feedId }).Count);
        }

        [TestMethod]
        public void MergeUpdatesWhenPublishedAtIsNewer()
        {
            TestableRssArticleCacheService service = CreateService();
            Guid feedId = Guid.NewGuid();
            service.MergeArticles(feedId, new[]
            {
                CreateArticle(feedId, "g:same", "Old title", DateTime.UtcNow.AddDays(-2), "old"),
            });

            int merged = service.MergeArticles(feedId, new[]
            {
                CreateArticle(feedId, "g:same", "New title", DateTime.UtcNow.AddDays(-1), "new"),
            });

            Assert.AreEqual(1, merged);
            RssCachedArticleModel loaded = service.LoadArticles(new[] { feedId }).Single();
            Assert.AreEqual("New title", loaded.Title);
            Assert.AreEqual("new", loaded.ContentHtml);
        }

        [TestMethod]
        public void MergeDoesNotUpdateWhenPublishedAtIsOlder()
        {
            TestableRssArticleCacheService service = CreateService();
            Guid feedId = Guid.NewGuid();
            service.MergeArticles(feedId, new[]
            {
                CreateArticle(feedId, "g:same", "Newer", DateTime.UtcNow.AddHours(-1), "new"),
            });

            int merged = service.MergeArticles(feedId, new[]
            {
                CreateArticle(feedId, "g:same", "Older", DateTime.UtcNow.AddHours(-5), "old"),
            });

            Assert.AreEqual(0, merged);
            Assert.AreEqual("Newer", service.LoadArticles(new[] { feedId }).Single().Title);
        }

        [TestMethod]
        public void RemoveFeedDeletesOnlyThatFeed()
        {
            TestableRssArticleCacheService service = CreateService();
            Guid feedA = Guid.NewGuid();
            Guid feedB = Guid.NewGuid();
            service.MergeArticles(feedA, new[] { CreateArticle(feedA, "g:a", "A", DateTime.UtcNow) });
            service.MergeArticles(feedB, new[] { CreateArticle(feedB, "g:b", "B", DateTime.UtcNow) });

            service.RemoveFeed(feedA);

            Assert.AreEqual(0, service.LoadArticles(new[] { feedA }).Count);
            Assert.AreEqual(1, service.LoadArticles(new[] { feedB }).Count);
        }

        [TestMethod]
        public void EnforceSizeLimitRemovesOldestArticles()
        {
            TestableRssArticleCacheService service = CreateService();
            Guid feedId = Guid.NewGuid();

            // Large content so a tiny byte limit forces pruning.
            string large = new string('x', 2000);
            service.MergeArticles(feedId, new[]
            {
                CreateArticle(feedId, "g:old", "Old", DateTime.UtcNow.AddDays(-3), large),
                CreateArticle(feedId, "g:mid", "Mid", DateTime.UtcNow.AddDays(-2), large),
                CreateArticle(feedId, "g:new", "New", DateTime.UtcNow.AddDays(-1), large),
            }, maxCacheBytes: 5000);

            IReadOnlyList<RssCachedArticleModel> loaded = service.LoadArticles(new[] { feedId });
            Assert.IsTrue(loaded.Count < 3);
            Assert.IsTrue(loaded.Any(a => a.ItemKey == "g:new"));
            Assert.IsFalse(loaded.Any(a => a.ItemKey == "g:old"));
        }

        private static RssCachedArticleModel CreateArticle(
            Guid feedId,
            string itemKey,
            string title,
            DateTime publishedAt,
            string content = "content")
        {
            return new RssCachedArticleModel
            {
                FeedId = feedId,
                ItemKey = itemKey,
                Title = title,
                Link = "https://example.com/" + itemKey,
                Summary = title,
                ContentHtml = content,
                PublishedAt = publishedAt,
                CachedAt = DateTime.UtcNow,
            };
        }

        private class TestableRssArticleCacheService : RssArticleCacheServiceBase
        {
            private readonly string _directory;

            public TestableRssArticleCacheService(string directory)
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
