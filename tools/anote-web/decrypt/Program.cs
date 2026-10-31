// Decrypt silentnotes_repository.silentnotes with a transfer code and emit Notes/Clip/Contacts JSON.
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SilentNotes.Crypto;
using VanillaCloudStorageClient;

static int Fail(string message, int code = 1)
{
    Console.Error.WriteLine(message);
    return code;
}

string cipherPath = null;
string transferCode = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--file" && i + 1 < args.Length)
        cipherPath = args[++i];
    else if (args[i] == "--code" && i + 1 < args.Length)
        transferCode = args[++i];
    else if (args[i] == "--help" || args[i] == "-h")
    {
        Console.WriteLine("Usage: AnoteDecrypt --file <path|-> --code <transfer-code>");
        Console.WriteLine("  Reads encrypted repository bytes from --file (or stdin when '-'),");
        Console.WriteLine("  decrypts with the transfer code, writes JSON to stdout.");
        return 0;
    }
}

if (string.IsNullOrWhiteSpace(transferCode))
    return Fail("Missing --code <transfer-code>");

string sanitized = SanitizeTransferCode(transferCode);
if (sanitized.Length < 5)
    return Fail("Transfer code looks too short after removing spaces/dashes.");

byte[] cipher;
try
{
    if (string.IsNullOrEmpty(cipherPath) || cipherPath == "-")
    {
        using var ms = new MemoryStream();
        Console.OpenStandardInput().CopyTo(ms);
        cipher = ms.ToArray();
    }
    else
    {
        cipher = File.ReadAllBytes(cipherPath);
    }
}
catch (Exception ex)
{
    return Fail("Could not read cipher: " + ex.Message);
}

if (cipher.Length == 0)
    return Fail("Cipher is empty.");

byte[] plain;
try
{
    var cryptor = new Cryptor("SilentNotes", null);
    using SecureString password = SecureStringExtensions.StringToSecureString(sanitized);
    plain = cryptor.Decrypt(cipher, password, out _);
}
catch (Exception ex)
{
    return Fail("Decrypt failed (wrong transfer code or corrupt file): " + ex.Message);
}

XDocument doc;
try
{
    using var stream = new MemoryStream(plain);
    doc = XDocument.Load(stream);
}
catch (Exception ex)
{
    return Fail("Decrypted payload is not valid XML: " + ex.Message);
}

XElement root = doc.Root;
if (root == null)
    return Fail("Empty XML repository.");

var notes = new List<object>();
foreach (XElement note in root.Element("notes")?.Elements("note") ?? Enumerable.Empty<XElement>())
{
    bool inBin = AttrBool(note, "in_recycling_bin");
    if (inBin)
        continue;

    string safeId = note.Element("safe")?.Value;
    bool locked = !string.IsNullOrWhiteSpace(safeId);
    string html = note.Element("html_content")?.Value ?? string.Empty;
    string title = locked ? "(locked note)" : ExtractTitle(html);
    var tags = note.Element("tags")?.Elements("tag").Select(t => t.Value).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
        ?? new List<string>();

    notes.Add(new
    {
        id = note.Attribute("id")?.Value,
        note_type = note.Attribute("note_type")?.Value,
        pinned = AttrBool(note, "note_pinned"),
        locked,
        background = note.Attribute("background_color")?.Value,
        created_at = note.Attribute("created_at")?.Value,
        modified_at = note.Attribute("modified_at")?.Value,
        tags,
        title,
        html = locked ? null : html,
    });
}

var clipboard = new List<object>();
foreach (XElement item in root.Element("clipboard")?.Elements("item") ?? Enumerable.Empty<XElement>())
{
    bool deleted = AttrBool(item, "deleted");
    if (deleted)
        continue;

    string kind = item.Attribute("kind")?.Value ?? "0";
    // Enum XmlSerializer writes integer by default (Text=0, Image=1).
    bool isText = kind is "0" or "Text" or "text";
    string text = item.Element("text")?.Value ?? string.Empty;
    if (!isText || string.IsNullOrEmpty(text))
        continue;

    clipboard.Add(new
    {
        id = item.Attribute("id")?.Value,
        created_utc = item.Attribute("created_utc")?.Value,
        modified_utc = item.Attribute("modified_utc")?.Value,
        favorite = AttrBool(item, "favorite"),
        kind = "text",
        text,
    });
}

var contacts = new List<object>();
foreach (XElement contact in root.Element("contacts")?.Elements("contact") ?? Enumerable.Empty<XElement>())
{
    var phones = contact.Element("phones")?.Elements("phone").Select(p => p.Value).Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
        ?? new List<string>();
    contacts.Add(new
    {
        id = contact.Attribute("id")?.Value,
        name = contact.Element("name")?.Value ?? string.Empty,
        phones,
        created_at = contact.Attribute("created_at")?.Value,
        modified_at = contact.Attribute("modified_at")?.Value,
    });
}

var payload = new
{
    ok = true,
    repository_id = root.Attribute("id")?.Value,
    revision = root.Attribute("revision")?.Value,
    notes,
    clipboard,
    contacts,
};

var options = new JsonSerializerOptions
{
    WriteIndented = false,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
Console.Write(JsonSerializer.Serialize(payload, options));
return 0;

static string SanitizeTransferCode(string code)
{
    return code.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
}

static bool AttrBool(XElement el, string name)
{
    string v = el.Attribute(name)?.Value;
    if (string.IsNullOrEmpty(v))
        return false;
    return v is "true" or "True" or "1";
}

static string ExtractTitle(string html)
{
    if (string.IsNullOrWhiteSpace(html))
        return "(empty note)";

    string text = Regex.Replace(html, "<[^>]+>", " ");
    text = System.Net.WebUtility.HtmlDecode(text);
    text = Regex.Replace(text, @"\s+", " ").Trim();
    if (string.IsNullOrEmpty(text))
        return "(empty note)";
    return text.Length <= 80 ? text : text.Substring(0, 80) + "…";
}
