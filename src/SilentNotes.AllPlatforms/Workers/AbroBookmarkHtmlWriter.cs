// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Writes Abro bookmarks as a portable <c>_f.html</c> + <c>_f.bookmarks.json</c> pair
    /// (same idea as desktop linqs/_f.html), plus <c>/sdcard/abro/bookmarks.json</c>.
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
                string json = BuildPortableJson(store);
                AtomicWriteText(sidecarPath, json);
                AtomicWriteText(htmlPath, BuildHtml(json));
                return htmlPath;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Writes linqs-style JSON to <paramref name="portablePath"/> (e.g. /sdcard/abro/bookmarks.json).</summary>
        public static bool TryWritePortableFile(string portablePath, AbroBookmarkStoreModel store)
        {
            if (string.IsNullOrWhiteSpace(portablePath))
                return false;
            try
            {
                string dir = Path.GetDirectoryName(portablePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                store ??= new AbroBookmarkStoreModel();
                EnsureDefaultGoogleBookmark(store);
                AtomicWriteText(portablePath, BuildPortableJson(store));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Parses linqs-style portable JSON into an Abro store model.</summary>
        public static bool TryParsePortable(string json, out AbroBookmarkStoreModel store)
        {
            store = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                JsonNode root = JsonNode.Parse(json);
                if (root is not JsonObject obj)
                    return false;

                var result = new AbroBookmarkStoreModel();
                if (obj["isMoveToTopEnabled"] != null)
                {
                    JsonValueKind kind = obj["isMoveToTopEnabled"].GetValueKind();
                    if (kind == JsonValueKind.False)
                        result.IsMoveToTopEnabled = false;
                    else if (kind == JsonValueKind.True)
                        result.IsMoveToTopEnabled = true;
                }

                if (obj["groups"] is JsonObject groupsObj)
                {
                    foreach (KeyValuePair<string, JsonNode> pair in groupsObj)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key))
                            continue;
                        string color = pair.Value?.GetValueKind() == JsonValueKind.String
                            ? pair.Value.GetValue<string>()
                            : "#2481cc";
                        result.Groups[pair.Key] = color ?? "#2481cc";
                    }
                }

                JsonArray bookmarks = obj["bookmarks"] as JsonArray ?? obj["items"] as JsonArray;
                if (bookmarks != null)
                {
                    foreach (JsonNode node in bookmarks)
                    {
                        if (node is not JsonObject item)
                            continue;
                        string url = (item["url"]?.GetValueKind() == JsonValueKind.String
                            ? item["url"]!.GetValue<string>()
                            : null) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(url))
                            continue;
                        string name = (item["name"]?.GetValueKind() == JsonValueKind.String
                            ? item["name"]!.GetValue<string>()
                            : null)
                            ?? (item["title"]?.GetValueKind() == JsonValueKind.String
                                ? item["title"]!.GetValue<string>()
                                : null)
                            ?? url;
                        string group = (item["group"]?.GetValueKind() == JsonValueKind.String
                            ? item["group"]!.GetValue<string>()
                            : null) ?? string.Empty;
                        result.Items.Add(new AbroBookmarkItemModel
                        {
                            Url = url.Trim(),
                            Title = (name ?? string.Empty).Trim(),
                            Group = (group ?? string.Empty).Trim(),
                            ModifiedAt = DateTime.UtcNow,
                        });
                    }
                }

                store = result;
                return true;
            }
            catch
            {
                store = null;
                return false;
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
            store.Groups ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (store.Items.Count > 0)
                return false;

            store.Items.Add(new AbroBookmarkItemModel
            {
                Title = "Google",
                Url = "https://www.google.com/",
                Group = "Abro",
                ModifiedAt = DateTime.UtcNow,
            });
            if (!store.Groups.ContainsKey("Abro"))
                store.Groups["Abro"] = "#2481cc";
            store.Touch();
            return true;
        }

        public static string BuildPortableJson(AbroBookmarkStoreModel store)
        {
            store ??= new AbroBookmarkStoreModel();
            var bookmarks = new List<object>();
            foreach (AbroBookmarkItemModel item in store.Items ?? new List<AbroBookmarkItemModel>())
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;
                bookmarks.Add(new
                {
                    name = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title,
                    url = item.Url,
                    group = item.Group ?? string.Empty,
                });
            }

            var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in store.Groups ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    groups[pair.Key] = string.IsNullOrWhiteSpace(pair.Value) ? "#2481cc" : pair.Value;
            }
            if (groups.Count == 0)
                groups["Abro"] = "#2481cc";

            var payload = new
            {
                groups,
                bookmarks,
                isMoveToTopEnabled = store.IsMoveToTopEnabled,
            };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static string BuildHtml(string sidecarJson)
        {
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
  --danger: #c44;
}
* { box-sizing: border-box; }
html, body {
  margin: 0; padding: 0;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  background: var(--bg); color: var(--text);
}
body { padding: 12px; }
.row { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 10px; }
.row.tight { margin-bottom: 6px; }
input, button, select {
  padding: 8px 10px; border-radius: 8px; border: 1px solid var(--border);
  background: var(--surface); color: var(--text); font-size: 14px;
}
input { flex: 1; min-width: 0; }
button {
  background: var(--accent); color: #fff; border: none; font-weight: 500; cursor: pointer; flex-shrink: 0;
}
button.secondary { background: #444; }
button.danger { background: var(--danger); }
#moveToTopToggleBtn[data-enabled="false"] { background: #555; }
.hint { color: var(--muted); font-size: 12px; margin: 0 0 10px; }
#stats {
  margin: 0 0 10px;
  color: var(--muted);
  font-size: 13px;
  font-weight: 500;
}
.add-bookmark-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 10px;
}
.add-bookmark-section .add-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  width: 100%;
}
.add-bookmark-section #bookmarkNameInput { flex: 1 1 100px; min-width: 90px; }
.add-bookmark-section #newBookmarkGroupSelect { flex: 0 1 120px; min-width: 90px; }
.add-bookmark-section #bookmarkUrlInput {
  width: 100%;
  flex: 1 1 100%;
  min-width: 0;
}
.search-go { display: flex; gap: 8px; align-items: center; width: 100%; }
.search-go input { flex: 1; }
.columns-wrapper {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 8px;
  margin-top: 8px;
}
.bookmark {
  display: flex; align-items: center; gap: 6px;
  padding: 6px 4px 6px 10px; border-radius: 8px;
  background: var(--surface); border: 1px solid var(--border);
  min-height: 36px;
}
.bookmark[data-group]:not([data-group=""]) {
  border-width: 2px;
  border-color: var(--group-color-variable, var(--accent));
}
.bookmark-icon { width: 18px; height: 18px; flex-shrink: 0; }
.bookmark-icon img { width: 100%; height: 100%; object-fit: contain; }
.bookmark-icon-fallback {
  width: 100%; height: 100%; border-radius: 50%; background: #4CAF50;
}
.bookmark-link {
  flex: 1 1 auto; min-width: 0; color: inherit; text-decoration: none;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-size: 14px;
}
/* Custom check control — native Android WebView checkboxes ignore size CSS. */
.bookmark-check {
  box-sizing: border-box;
  flex: 0 0 20px;
  width: 20px !important;
  height: 20px !important;
  min-width: 20px !important;
  max-width: 20px !important;
  min-height: 20px !important;
  max-height: 20px !important;
  margin: 0 !important;
  padding: 0 !important;
  border: 1.5px solid #cfcfcf !important;
  border-radius: 3px;
  background: transparent !important;
  color: transparent;
  font-size: 12px;
  line-height: 1;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  align-self: center;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}
.bookmark-check[aria-checked="true"] {
  background: var(--accent) !important;
  border-color: var(--accent) !important;
  color: #fff;
}
.bookmark-check[aria-checked="true"]::before {
  content: "✓";
  font-size: 12px;
  line-height: 1;
  color: #fff;
}
.hidden { display: none !important; }
</style>
</head>
<body>
<p id="stats">Total bookmarks: 0</p>

<div class="row tight">
  <button type="button" id="moveToTopToggleBtn" data-enabled="true" onclick="toggleMoveToTopMode()">Move to Top: ON</button>
  <button type="button" class="secondary" onclick="exportPortable()">Export bookmarks.json</button>
  <button type="button" class="secondary" onclick="importPortable()">Import bookmarks.json</button>
</div>

<div class="add-bookmark-section">
  <div class="add-row">
    <input id="bookmarkNameInput" type="text" placeholder="Name" autocomplete="off">
    <select id="newBookmarkGroupSelect"><option value="">No group</option></select>
    <button type="button" id="addBookmarkBtn" onclick="saveBookmark()">Add</button>
    <button type="button" class="danger" id="deleteClearBtn" onclick="deleteOrClear()">Delete/Clear</button>
  </div>
  <div class="add-row">
    <input id="bookmarkUrlInput" type="text" placeholder="https://..." autocomplete="off">
  </div>
</div>

<div class="row">
  <input id="searchInput" type="search" placeholder="Search bookmarks..." autocomplete="off">
</div>
<div class="row">
  <div class="search-go">
    <input id="yandexInput" type="search" placeholder="Yandex search..." autocomplete="off">
    <button type="button" onclick="searchYandex()">Go</button>
  </div>
</div>
<div class="row">
  <div class="search-go">
    <input id="startpageInput" type="search" placeholder="Startpage search..." autocomplete="off">
    <button type="button" onclick="searchStartpage()">Go</button>
  </div>
</div>

<div class="columns-wrapper" id="columnsContainer"></div>

<script>
const FAVICON_API = 'https://www.google.com/s2/favicons?sz=64&domain=';
const EMBEDDED_DATA_JSON = `
"""
                + escapedJson
                + """
`;

let allBookmarks = [];
let groups = {};
let filteredBookmarks = [];
let editingBookmarkUrl = null;
let isMoveToTopEnabled = true;

const nameInput = document.getElementById('bookmarkNameInput');
const urlInput = document.getElementById('bookmarkUrlInput');
const groupSelectInput = document.getElementById('newBookmarkGroupSelect');
const addModifyBtn = document.getElementById('addBookmarkBtn');
const searchInput = document.getElementById('searchInput');

function hasBridge() {
  return !!(window.AbroBm && typeof window.AbroBm.openTab === 'function');
}

function openInNewTab(url) {
  if (!url) return;
  if (hasBridge()) {
    try { window.AbroBm.openTab(url); return; } catch (e) {}
  }
  window.open(url, '_blank');
}

function normalizeBookmarkPayload(data) {
  const g = (data && data.groups && typeof data.groups === 'object') ? data.groups : {};
  const list = Array.isArray(data && data.bookmarks) ? data.bookmarks
    : (Array.isArray(data && data.items) ? data.items.map(function (i) {
        return { name: i.title || i.name || i.url, url: i.url, group: i.group || '' };
      }) : []);
  const move = data && typeof data.isMoveToTopEnabled === 'boolean' ? data.isMoveToTopEnabled : true;
  return {
    groups: g,
    bookmarks: list.filter(function (b) { return b && b.url; }).map(function (b) {
      return { name: b.name || b.title || b.url, url: b.url, group: b.group || '' };
    }),
    isMoveToTopEnabled: move
  };
}

function buildPortableExportObject() {
  return { groups: groups, bookmarks: allBookmarks, isMoveToTopEnabled: isMoveToTopEnabled };
}

function persist() {
  const json = JSON.stringify(buildPortableExportObject());
  try { localStorage.setItem('abro_bookmarks_portable', json); } catch (e) {}
  if (hasBridge() && typeof window.AbroBm.persist === 'function') {
    try { window.AbroBm.persist(json); } catch (e) {}
  }
}

function exportPortable() {
  persist();
  if (hasBridge() && typeof window.AbroBm.exportFile === 'function') {
    try {
      const ok = window.AbroBm.exportFile();
      alert(ok ? 'Exported to /sdcard/abro/bookmarks.json' : 'Export failed');
      return;
    } catch (e) {}
  }
  alert('Export bridge unavailable');
}

function importPortable() {
  if (hasBridge() && typeof window.AbroBm.importFile === 'function') {
    try {
      const json = window.AbroBm.importFile();
      if (!json) { alert('No /sdcard/abro/bookmarks.json found'); return; }
      const norm = normalizeBookmarkPayload(JSON.parse(json));
      groups = norm.groups;
      allBookmarks = norm.bookmarks;
      isMoveToTopEnabled = norm.isMoveToTopEnabled;
      editingBookmarkUrl = null;
      persist();
      renderGroups();
      updateMoveToTopButton();
      filterBookmarks(searchInput.value);
      alert('Imported from /sdcard/abro/bookmarks.json');
      return;
    } catch (e) {
      alert('Import failed: ' + e);
      return;
    }
  }
  alert('Import bridge unavailable');
}

function toggleMoveToTopMode() {
  isMoveToTopEnabled = !isMoveToTopEnabled;
  persist();
  updateMoveToTopButton();
}

function updateMoveToTopButton() {
  const btn = document.getElementById('moveToTopToggleBtn');
  if (!btn) return;
  btn.textContent = isMoveToTopEnabled ? 'Move to Top: ON' : 'Move to Top: OFF';
  btn.setAttribute('data-enabled', isMoveToTopEnabled ? 'true' : 'false');
}

function moveBookmarkToTop(url) {
  if (!isMoveToTopEnabled) return;
  const index = allBookmarks.findIndex(function (bm) { return bm.url === url; });
  if (index < 0) return;
  const item = allBookmarks.splice(index, 1)[0];
  allBookmarks.unshift(item);
  persist();
  filterBookmarks(searchInput.value);
}

function handleBookmarkClick(e, url) {
  if (e.target && e.target.closest && e.target.closest('.bookmark-check'))
    return false;
  e.preventDefault();
  e.stopPropagation();
  moveBookmarkToTop(url);
  setTimeout(function () { openInNewTab(url); }, 10);
  return false;
}

function renderGroups() {
  const names = Object.keys(groups || {});
  groupSelectInput.innerHTML = '<option value="">No group</option>';
  names.forEach(function (name) {
    const opt = document.createElement('option');
    opt.value = name;
    opt.textContent = name;
    groupSelectInput.appendChild(opt);
  });
  if (editingBookmarkUrl) {
    const bm = allBookmarks.find(function (b) { return b.url === editingBookmarkUrl; });
    groupSelectInput.value = (bm && bm.group) || '';
  }
}

function resetEditingState() {
  nameInput.value = '';
  urlInput.value = '';
  groupSelectInput.value = '';
  editingBookmarkUrl = null;
  addModifyBtn.textContent = 'Add';
  document.querySelectorAll('.bookmark-check[aria-checked="true"]').forEach(function (btn) {
    btn.setAttribute('aria-checked', 'false');
  });
}

function toggleBookmarkCheck(url, btn) {
  const willCheck = btn.getAttribute('aria-checked') !== 'true';
  document.querySelectorAll('.bookmark-check[aria-checked="true"]').forEach(function (other) {
    if (other !== btn) other.setAttribute('aria-checked', 'false');
  });
  if (willCheck) {
    btn.setAttribute('aria-checked', 'true');
    const bookmark = allBookmarks.find(function (bm) { return bm.url === url; });
    if (bookmark) {
      nameInput.value = bookmark.name;
      urlInput.value = bookmark.url;
      groupSelectInput.value = bookmark.group || '';
      editingBookmarkUrl = url;
      addModifyBtn.textContent = 'Modify';
      nameInput.focus();
    }
  } else {
    btn.setAttribute('aria-checked', 'false');
    if (editingBookmarkUrl === url)
      resetEditingState();
  }
  filterBookmarks(searchInput.value);
}

function saveBookmark() {
  let name = nameInput.value.trim();
  let url = urlInput.value.trim();
  const group = groupSelectInput.value || '';
  if (!name || !url) { alert('Please enter both name and URL'); return; }
  if (!/^https?:\/\//i.test(url)) url = 'https://' + url;

  if (editingBookmarkUrl) {
    const index = allBookmarks.findIndex(function (bm) { return bm.url === editingBookmarkUrl; });
    if (index >= 0) {
      allBookmarks[index].name = name;
      allBookmarks[index].url = url;
      allBookmarks[index].group = group;
      editingBookmarkUrl = url;
    }
  } else {
    allBookmarks.push({ name: name, url: url, group: group });
  }
  if (group && !groups[group]) groups[group] = '#2481cc';
  resetEditingState();
  persist();
  renderGroups();
  filterBookmarks(searchInput.value);
}

function deleteOrClear() {
  if (editingBookmarkUrl) {
    const url = editingBookmarkUrl;
    allBookmarks = allBookmarks.filter(function (bm) { return bm.url !== url; });
    resetEditingState();
    persist();
    filterBookmarks(searchInput.value);
    return;
  }
  resetEditingState();
}

function extractDomain(url) {
  try { return new URL(url).hostname.replace(/^www\./, ''); } catch (e) { return null; }
}

function getFaviconUrl(url) {
  const domain = extractDomain(url);
  if (!domain) {
    return 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><text x="8" y="12" text-anchor="middle" fill="%23999" font-size="12">📌</text></svg>';
  }
  return FAVICON_API + encodeURIComponent(domain);
}

function escapeHtml(s) {
  return String(s || '').replace(/[&<>"']/g, function (c) {
    return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
  });
}

function renderBookmarks(items) {
  const container = document.getElementById('columnsContainer');
  if (!items.length) {
    container.innerHTML = '<div class="hint">No bookmarks</div>';
    return;
  }
  container.innerHTML = items.map(function (bm) {
    const faviconUrl = getFaviconUrl(bm.url);
    const groupColor = bm.group && groups[bm.group] ? groups[bm.group] : '';
    const selected = bm.url === editingBookmarkUrl ? 'true' : 'false';
    const safeUrl = String(bm.url || '').replace(/\\/g, '\\\\').replace(/'/g, "\\'");
    return '<div class="bookmark" data-group="' + escapeHtml(bm.group || '') + '" style="--group-color-variable:' + escapeHtml(groupColor) + '">'
      + '<div class="bookmark-icon"><img src="' + faviconUrl + '" alt=""></div>'
      + '<a class="bookmark-link" href="' + escapeHtml(bm.url) + '" target="_blank" rel="noopener noreferrer"'
      + ' onclick="return handleBookmarkClick(event, \'' + safeUrl + '\')">' + escapeHtml(bm.name) + '</a>'
      + '<button type="button" class="bookmark-check" aria-checked="' + selected + '"'
      + ' aria-label="Select bookmark" onclick="event.stopPropagation(); toggleBookmarkCheck(\'' + safeUrl + '\', this)"></button>'
      + '</div>';
  }).join('');

  container.querySelectorAll('.bookmark-icon img').forEach(function (img) {
    img.addEventListener('error', function () {
      this.parentElement.innerHTML = '<div class="bookmark-icon-fallback"></div>';
    });
  });
}

function filterBookmarks(query) {
  const q = (query || '').trim().toLowerCase();
  filteredBookmarks = !q ? allBookmarks : allBookmarks.filter(function (bm) {
    return (bm.name || '').toLowerCase().indexOf(q) >= 0
      || (bm.url || '').toLowerCase().indexOf(q) >= 0
      || (bm.group || '').toLowerCase().indexOf(q) >= 0;
  });
  renderBookmarks(filteredBookmarks);
  const stats = document.getElementById('stats');
  stats.textContent = q
    ? ('Showing ' + filteredBookmarks.length + ' of ' + allBookmarks.length)
    : ('Total bookmarks: ' + allBookmarks.length);
}

function searchYandex() {
  const query = document.getElementById('yandexInput').value.trim();
  if (!query) return;
  openInNewTab('https://ya.ru/search?text=' + encodeURIComponent(query));
  document.getElementById('yandexInput').value = '';
}

function searchStartpage() {
  const query = document.getElementById('startpageInput').value.trim();
  if (!query) return;
  openInNewTab('https://www.startpage.com/do/search?q=' + encodeURIComponent(query));
  document.getElementById('startpageInput').value = '';
}

searchInput.addEventListener('input', function () { filterBookmarks(searchInput.value); });
document.getElementById('yandexInput').addEventListener('keypress', function (e) {
  if (e.key === 'Enter') searchYandex();
});
document.getElementById('startpageInput').addEventListener('keypress', function (e) {
  if (e.key === 'Enter') searchStartpage();
});

(function init() {
  let data;
  try { data = normalizeBookmarkPayload(JSON.parse(EMBEDDED_DATA_JSON)); }
  catch (e) { data = null; }
  if (!data || !data.bookmarks || !data.bookmarks.length) {
    try {
      const saved = localStorage.getItem('abro_bookmarks_portable');
      if (saved) data = normalizeBookmarkPayload(JSON.parse(saved));
    } catch (e) {}
  }
  if (!data) data = { groups: { Abro: '#2481cc' }, bookmarks: [], isMoveToTopEnabled: true };
  groups = data.groups || {};
  allBookmarks = data.bookmarks || [];
  isMoveToTopEnabled = data.isMoveToTopEnabled !== false;
  filteredBookmarks = allBookmarks;
  renderGroups();
  updateMoveToTopButton();
  filterBookmarks('');
  persist();
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
