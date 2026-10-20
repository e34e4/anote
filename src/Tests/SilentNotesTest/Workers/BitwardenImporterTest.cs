using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class BitwardenImporterTest
    {
        [TestMethod]
        public void ImportJsonLogins()
        {
            string text =
                "{" +
                "\"encrypted\":false," +
                "\"items\":[" +
                "{\"type\":1,\"name\":\"Example\",\"notes\":\"n\"," +
                "\"login\":{\"username\":\"user\",\"password\":\"secret\"," +
                "\"uris\":[{\"uri\":\"https://example.com\"}]}}" +
                ",{\"type\":2,\"name\":\"NoteOnly\",\"notes\":\"skip\"}" +
                "]}";

            List<PasswordEntryModel> result = BitwardenImporter.Import(text);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Example", result[0].Name);
            Assert.AreEqual("user", result[0].Username);
            Assert.AreEqual("secret", result[0].Password);
            Assert.AreEqual("https://example.com", result[0].Uris[0]);
            Assert.AreEqual("n", result[0].Notes);
        }

        [TestMethod]
        public void ImportJsonRoundtripsThroughXml()
        {
            string text =
                "{\"items\":[{\"type\":1,\"name\":\"A\",\"login\":{\"username\":\"u\",\"password\":\"p\"," +
                "\"uris\":[{\"uri\":\"https://a.test\"}]}}]}";
            List<PasswordEntryModel> imported = BitwardenImporter.Import(text);
            var vault = new PasswordVaultModel();
            vault.Entries.AddRange(imported);

            byte[] xml = XmlUtils.SerializeToXmlBytes(vault);
            PasswordVaultModel roundtrip = XmlUtils.DeserializeFromXmlDocument<PasswordVaultModel>(
                XmlUtils.LoadFromXmlBytes(xml));

            Assert.AreEqual(1, roundtrip.Entries.Count);
            Assert.AreEqual("A", roundtrip.Entries[0].Name);
            Assert.AreEqual("u", roundtrip.Entries[0].Username);
            Assert.AreEqual("p", roundtrip.Entries[0].Password);
            Assert.AreEqual("https://a.test", roundtrip.Entries[0].Uris[0]);
        }

        [TestMethod]
        public void IsDuplicateMatchesNameUserAndFirstUri()
        {
            var existing = new PasswordEntryModel { Name = "X", Username = "u" };
            existing.Uris.Add("https://x.test");
            var candidate = new PasswordEntryModel { Name = "x", Username = "U" };
            candidate.Uris.Add("https://x.test");
            var other = new PasswordEntryModel { Name = "X", Username = "u" };
            other.Uris.Add("https://other.test");

            Assert.IsTrue(BitwardenImporter.IsDuplicate(existing, candidate));
            Assert.IsFalse(BitwardenImporter.IsDuplicate(existing, other));
        }
    }
}
