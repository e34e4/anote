// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Writes Abro bookmarks as a portable <c>_f.html</c> + <c>_f.bookmarks.json</c> pair
    /// (same idea as desktop linqs/_f.html).
    /// </summary>
    public static class AbroBookmarkHtmlWriter
    {
        public const string HtmlFileName = "_f.html";
        public const string SidecarFileName = "_f.bookmarks.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Ensures the bookmarks directory contains <paramref name="store"/> as HTML + JSON sidecar.
        /// Returns the absolute path of <c>_f.html</c>, or null on failure.
        /// </summary>
        public static string TryWrite(string bookmarksDirectory, AbroBookmarkStoreModel store)
        {
            if (string.IsNullOrWhiteSpace(bookmarksDirectory))
                return null;

            try
            {
                Directory.CreateDirectory(bookmarksDirectory);
                store ??= new AbroBookmarkStoreModel();
                EnsureDefaultGoogleBookmark(store);

                string sidecarPath = Path.Combine(bookmarksDirectory, SidecarFileName);
                string htmlPath = Path.Combine(bookmarksDirectory, HtmlFileName);
                string json = BuildSidecarJson(store);
                AtomicWriteText(sidecarPath, json);
                AtomicWriteText(htmlPath, BuildHtml(json, store));
                return htmlPath;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Adds a default Google bookmark when the store is empty.
        /// </summary>
        public static bool EnsureDefaultGoogleBookmark(AbroBookmarkStoreModel store)
        {
            if (store == null)
                return false;
            store.Items ??= new List<AbroBookmarkItemModel>();
            if (store.Items.Count > 0)
                return false;

            store.Items.Add(new AbroBookmarkItemModel
            {
                Title = "Google",
                Url = "https://www.google.com/",
                ModifiedAt = DateTime.UtcNow,
            });
            store.Touch();
            return true;
        }

        private static string BuildSidecarJson(AbroBookmarkStoreModel store)
        {
            var bookmarks = new List<object>();
            foreach (AbroBookmarkItemModel item in store.Items ?? new List<AbroBookmarkItemModel>())
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;
                bookmarks.Add(new
                {
                    name = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title,
                    url = item.Url,
                    group = "Abro",
                });
            }

            var payload = new
            {
                groups = new Dictionary<string, string> { ["Abro"] = "#2481cc" },
                bookmarks,
                isMoveToTopEnabled = true,
            };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static string BuildHtml(string sidecarJson, AbroBookmarkStoreModel store)
        {
            var links = new StringBuilder();
            foreach (AbroBookmarkItemModel item in (store.Items ?? new List<AbroBookmarkItemModel>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                .OrderBy(i => string.IsNullOrWhiteSpace(i.Title) ? i.Url : i.Title, StringComparer.OrdinalIgnoreCase))
            {
                string name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title);
                string url = WebUtility.HtmlEncode(item.Url);
                links.Append("<a class=\"bookmark\" href=\"")
                    .Append(url)
                    .Append("\">")
                    .Append(name)
                    .Append("</a>\n");
            }

            string escapedJson = sidecarJson
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("`", "\\`", StringComparison.Ordinal)
                .Replace("</", "<\\/", StringComparison.Ordinal);

            return """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Abro bookmarks</title>
<style>
:root {
  --bg: #0b0b0c;
  --surface: #161618;
  --text: #f2f2f2;
  --muted: #9a9aa0;
  --accent: #2481cc;
  --border: #2c2c2e;
}
* { box-sizing: border-box; }
html, body {
  margin: 0; padding: 0;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  background: var(--bg); color: var(--text);
}
body { padding: 16px; }
h1 { margin: 0 0 8px; font-size: 22px; font-weight: 600; }
.hint { color: var(--muted); font-size: 13px; margin-bottom: 16px; }
#search {
  width: 100%; max-width: 480px; margin-bottom: 16px;
  padding: 10px 12px; border-radius: 8px; border: 1px solid var(--border);
  background: var(--surface); color: var(--text); font-size: 15px;
}
.grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 8px;
}
.bookmark {
  display: block; padding: 12px; border-radius: 8px;
  background: var(--surface); border: 1px solid var(--border);
  color: var(--text); text-decoration: none; word-break: break-word;
  font-size: 14px; line-height: 1.3;
}
.bookmark:hover, .bookmark:focus { border-color: var(--accent); color: var(--accent); }
.hidden { display: none !important; }
#stats { margin-top: 16px; color: var(--muted); font-size: 12px; }
</style>
</head>
<body>
<h1>Abro bookmarks</h1>
<p class="hint">Portable page like desktop <code>_f.html</code>. Sidecar: <code>_f.bookmarks.json</code>.</p>
<input id="search" type="search" placeholder="Search bookmarks..." autocomplete="off">
<div class="grid" id="list">
"""
                + links
                + """
</div>
<p id="stats"></p>
<script>
const EMBEDDED_DATA_JSON = `
"""
                + escapedJson
                + """
`;
(function () {
  const search = document.getElementById('search');
  const list = document.getElementById('list');
  const stats = document.getElementById('stats');
  const items = Array.from(list.querySelectorAll('.bookmark'));
  function refresh() {
    const q = (search.value || '').trim().toLowerCase();
    let shown = 0;
    items.forEach(function (a) {
      const text = (a.textContent || '').toLowerCase() + ' ' + (a.getAttribute('href') || '').toLowerCase();
      const ok = !q || text.indexOf(q) >= 0;
      a.classList.toggle('hidden', !ok);
      if (ok) shown++;
    });
    stats.textContent = q
      ? ('Showing ' + shown + ' of ' + items.length + ' bookmarks')
      : ('Total bookmarks: ' + items.length);
  }
  search.addEventListener('input', refresh);
  refresh();
  try { JSON.parse(EMBEDDED_DATA_JSON); } catch (e) {}
})();
</script>
</body>
</html>
""";
        }

        private static void AtomicWriteText(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content ?? string.Empty, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }
    }
}
