// Copyright © 2026 aNote.
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
    }
}
