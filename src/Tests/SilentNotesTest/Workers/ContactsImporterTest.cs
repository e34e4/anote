using Microsoft.VisualStudio.TestTools.UnitTesting;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotesTest.Workers
{
    [TestClass]
    public class ContactsImporterTest
    {
        [TestMethod]
        public void ImportFromExportedHtml()
        {
            // Content mirrors a real contacts.html exported by the contacts app.
            string text =
                "<!DOCTYPE html>\n<html><head><title>Contacts</title></head><body>\n" +
                "<script>\n" +
                "const SEED = [{\"name\":\"!Qu\",\"phones\":[\"+79110052782\"]}," +
                "{\"name\":\"Служба газа\",\"phones\":[\"104\"]}," +
                "{\"name\":\"13kadirovaElVikt\",\"phones\":[\"+79119911114\",\"2463267\"]}];\n" +
                "const STORAGE_KEY = \"stommis-contacts\";\n" +
                "let contacts = cloneSeed();\n" +
                "</script>\n</body></html>";

            ContactListModel result = ContactsImporter.Import(text);

            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("!Qu", result[0].Name);
            Assert.AreEqual("+79110052782", result[0].Phones[0]);
            Assert.AreEqual("Служба газа", result[1].Name);
            Assert.AreEqual(2, result[2].Phones.Count);
            Assert.AreEqual("2463267", result[2].Phones[1]);
            Assert.AreNotEqual(System.Guid.Empty, result[0].Id);
        }

        [TestMethod]
        public void ImportFromPlainJsonArray()
        {
            string text = "[{\"name\":\"Alice\",\"phones\":[\"111\",\"222\"]},{\"name\":\"Bob\",\"phones\":[]}]";

            ContactListModel result = ContactsImporter.Import(text);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Alice", result[0].Name);
            Assert.AreEqual(2, result[0].Phones.Count);
            Assert.AreEqual(0, result[1].Phones.Count);
        }

        [TestMethod]
        public void ImportIgnoresBracketsInsideStrings()
        {
            string text = "const SEED = [{\"name\":\"a]b[c\",\"phones\":[\"+7\"]}];\nnext();";

            ContactListModel result = ContactsImporter.Import(text);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("a]b[c", result[0].Name);
        }

        [TestMethod]
        public void ImportAcceptsNumericPhones()
        {
            string text = "const SEED = [{\"name\":\"x\",\"phones\":[104,7911]}];";

            ContactListModel result = ContactsImporter.Import(text);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result[0].Phones.Count);
            Assert.AreEqual("104", result[0].Phones[0]);
        }

        [TestMethod]
        public void ImportMalformedReturnsNull()
        {
            Assert.IsNull(ContactsImporter.Import(null));
            Assert.IsNull(ContactsImporter.Import(string.Empty));
            Assert.IsNull(ContactsImporter.Import("there is no list in this file"));
            Assert.IsNull(ContactsImporter.Import("const SEED = [not valid json];"));
        }
    }
}
