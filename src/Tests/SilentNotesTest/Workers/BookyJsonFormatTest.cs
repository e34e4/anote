using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class BookyJsonFormatTest
    {
        [TestMethod]
        public void ParseReadsWrappedFile()
        {
            string json = @"{
              ""version"": 1,
              ""updatedAt"": ""2026-01-01T10:00:00.000Z"",
              ""bookmarks"": [
                { ""id"": ""11111111-1111-1111-1111-111111111111"", ""createdAt"": ""2026-01-01T09:00:00.000Z"",
                  ""updatedAt"": ""2026-01-01T09:30:00.000Z"", ""title"": ""Example"", ""url"": ""https://example.com"",
                  ""favIconUrl"": ""https://example.com/favicon.ico"", ""note"": ""hi"",
                  ""anchor"": { ""quote"": ""selected"", ""prefix"": ""before"", ""suffix"": ""after"", ""scrollY"": 120, ""scrollRatio"": 0.4 } }
              ]
            }";

            BookmarkListModel result = BookyJsonFormat.Parse(json);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            BookmarkModel bookmark = result[0];
            Assert.AreEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), bookmark.Id);
            Assert.AreEqual(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), bookmark.CreatedAt);
            Assert.AreEqual(new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc), bookmark.ModifiedAt);
            Assert.AreEqual("Example", bookmark.Title);
            Assert.AreEqual("https://example.com", bookmark.Url);
            Assert.AreEqual("https://example.com/favicon.ico", bookmark.FavIconUrl);
            Assert.AreEqual("hi", bookmark.Note);
            Assert.AreEqual("selected", bookmark.Quote);
            Assert.IsNotNull(bookmark.AnchorJson);
        }

        [TestMethod]
        public void ParseAcceptsBareArrayAndUnknownFields()
        {
            string json = @"[ { ""id"": ""22222222-2222-2222-2222-222222222222"", ""url"": ""https://a"", ""somethingNew"": 42 } ]";

            BookmarkListModel result = BookyJsonFormat.Parse(json);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("https://a", result[0].Url);
        }

        [TestMethod]
        public void SerializePreservesAnchorDetailsAndFavicon()
        {
            string json = @"[{ ""id"": ""33333333-3333-3333-3333-333333333333"", ""createdAt"": ""2026-01-01T09:00:00.000Z"",
                ""updatedAt"": ""2026-01-01T09:30:00.000Z"", ""title"": ""T"", ""url"": ""https://b"",
                ""favIconUrl"": ""https://b/f.ico"", ""note"": ""n"",
                ""anchor"": { ""quote"": ""q"", ""prefix"": ""p"", ""suffix"": ""s"", ""scrollY"": 99, ""scrollRatio"": 0.77,
                              ""rect"": { ""top"": 1, ""left"": 2, ""width"": 3, ""height"": 4 } } }]";
            BookmarkListModel parsed = BookyJsonFormat.Parse(json);

            string output = BookyJsonFormat.Serialize(parsed);
            JsonNode root = JsonNode.Parse(output);
            JsonObject item = (JsonObject)root["bookmarks"][0];
            JsonObject anchor = (JsonObject)item["anchor"];

            Assert.AreEqual(1, (int)root["version"]);
            Assert.AreEqual("33333333-3333-3333-3333-333333333333", (string)item["id"]);
            Assert.AreEqual("https://b/f.ico", (string)item["favIconUrl"]);
            Assert.AreEqual("q", (string)anchor["quote"]);
            Assert.AreEqual("p", (string)anchor["prefix"]);
            Assert.AreEqual("s", (string)anchor["suffix"]);
            Assert.AreEqual(99, (int)anchor["scrollY"]);
            Assert.AreEqual(0.77, (double)anchor["scrollRatio"], 0.0001);
            Assert.AreEqual(4, (int)anchor["rect"]["height"]);
        }

        [TestMethod]
        public void SerializeQuotesLocalBookmarkWithoutAnchorJson()
        {
            var bookmark = new BookmarkModel
            {
                Title = "note",
                Url = "https://c",
                Quote = "the quote",
                CreatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
                ModifiedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            };

            string output = BookyJsonFormat.Serialize(new[] { bookmark });
            JsonObject item = (JsonObject)JsonNode.Parse(output)["bookmarks"][0];
            JsonObject anchor = (JsonObject)item["anchor"];

            Assert.AreEqual("the quote", (string)anchor["quote"]);
            Assert.AreEqual("2026-01-01T08:00:00.000Z", (string)item["createdAt"]);
            Assert.AreEqual(bookmark.Id.ToString(), (string)item["id"]);
        }

        [TestMethod]
        public void SerializeOmitsAnchorWhenNoQuote()
        {
            var bookmark = new BookmarkModel { Url = "https://d", Quote = null };
            string output = BookyJsonFormat.Serialize(new[] { bookmark });
            JsonObject item = (JsonObject)JsonNode.Parse(output)["bookmarks"][0];
            Assert.IsNull(item["anchor"]);
        }

        [TestMethod]
        public void ParseInvalidReturnsNull()
        {
            Assert.IsNull(BookyJsonFormat.Parse("not json"));
            Assert.IsNull(BookyJsonFormat.Parse("42"));
            Assert.AreEqual(0, BookyJsonFormat.Parse(string.Empty).Count);
        }
    }
}
