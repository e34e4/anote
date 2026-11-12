// Copyright © 2026 aNote.
using System;
using System.IO;
using NUnit.Framework;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestFixture]
    public class YtDlpUrlHelperTest
    {
        [Test]
        public void NormalizeDownloadUrl_RewritesVimeoToPlayer()
        {
            string result = YtDlpUrlHelper.NormalizeDownloadUrl("https://vimeo.com/1234190827?fl=pl&fe=sh");
            Assert.AreEqual("https://player.vimeo.com/video/1234190827", result);
        }

        [Test]
        public void NormalizeDownloadUrl_PreservesVimeoHash()
        {
            string result = YtDlpUrlHelper.NormalizeDownloadUrl("https://vimeo.com/1118190054/173a644b6d");
            Assert.AreEqual("https://player.vimeo.com/video/1118190054?h=173a644b6d", result);
        }

        [Test]
        public void NormalizeDownloadUrl_LeavesVkvideoUnchanged()
        {
            const string url = "https://m.vkvideo.ru/video-186221654_456239076";
            Assert.AreEqual(url, YtDlpUrlHelper.NormalizeDownloadUrl(url));
        }

        [Test]
        public void TryParseVkVideoId_FromMobileVkvideo()
        {
            Assert.IsTrue(YtDlpUrlHelper.TryParseVkVideoId(
                "https://m.vkvideo.ru/video-186221654_456239076", out string id));
            Assert.AreEqual("-186221654_456239076", id);
        }

        [Test]
        public void PickBestVkSource_PrefersMp4OverHls()
        {
            const string html =
                "<source src=\"https://cdn.example/video.m3u8\" type=\"application/vnd.apple.mpegurl\" />" +
                "<source src=\"https://cdn.example/low.mp4\" type=\"video/mp4\" />" +
                "<source src=\"https://cdn.example/high.mp4\" type=\"video/mp4\" />";
            string url = YtDlpUrlHelper.PickBestVkSource(html);
            Assert.AreEqual("https://cdn.example/high.mp4", url);
        }

        [Test]
        public void BuildSocks5ProxyArgument_IncludesAuth()
        {
            string proxy = YtDlpUrlHelper.BuildSocks5ProxyArgument("127.0.0.1", 1080, "u", "p");
            Assert.AreEqual("socks5://u:p@127.0.0.1:1080", proxy);
        }

        [Test]
        public void WriteNetscapeCookiesFile_HasNoUtf8Bom()
        {
            string path = Path.Combine(Path.GetTempPath(), "ytdlp-cookies-test-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                YtDlpUrlHelper.WriteNetscapeCookiesFile(path, new[]
                {
                    (".reddit.com", "loid=abc; session=1"),
                });
                byte[] raw = File.ReadAllBytes(path);
                Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF);
                string text = File.ReadAllText(path);
                Assert.True(text.StartsWith("# Netscape HTTP Cookie File", StringComparison.Ordinal));
                Assert.True(text.Contains("loid"));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Test]
        public void PickBestVkSource_FindsEscapedOkcdnUrl()
        {
            const string json =
                "{\"payload\":[0,[\"x\",\"<source src=\\\"https://vkvd.okcdn.ru/video.mp4?x=1\\\" type=\\\"video/mp4\\\" />\"]]}";
            Assert.AreEqual("https://vkvd.okcdn.ru/video.mp4?x=1", YtDlpUrlHelper.PickBestVkSource(json));
        }
    }
}
