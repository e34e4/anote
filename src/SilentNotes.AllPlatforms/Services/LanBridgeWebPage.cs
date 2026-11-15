// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

namespace SilentNotes.Services
{
    /// <summary>
    /// The single page web app which is served by the <see cref="LanBridgeService"/>. It is
    /// deliberately dependency free and renders all note content as text, never as html, so a
    /// note cannot execute scripts in the browser.
    /// </summary>
    public static class LanBridgeWebPage
    {
        /// <summary>Gets the html source of the drop page.</summary>
        public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>aNote LAN bridge</title>
<style>
  :root { color-scheme: dark; --bg:#0d0d0d; --panel:#181818; --line:#2c2c2c; --text:#e8e8e8; --muted:#9a9a9a; --accent:#26a69a; }
  * { box-sizing:border-box; }
  body { margin:0; font:14px/1.45 system-ui, -apple-system, Segoe UI, Roboto, sans-serif; background:var(--bg); color:var(--text); }
  header { padding:16px 20px; border-bottom:1px solid var(--line); display:flex; align-items:center; gap:12px; flex-wrap:wrap; }
  h1 { font-size:17px; margin:0; font-weight:600; letter-spacing:.2px; }
  .status { font-size:12px; color:var(--muted); margin-left:auto; }
  .dot { display:inline-block; width:8px; height:8px; border-radius:50%; background:#c62828; margin-right:6px; vertical-align:middle; }
  .dot.on { background:#43a047; }
  nav { display:flex; gap:8px; padding:12px 20px 0; }
  nav button { flex:1; padding:10px; border-radius:10px; border:1px solid var(--line); background:var(--panel); color:var(--text); font-size:14px; cursor:pointer; }
  nav button.active { background:var(--accent); border-color:var(--accent); color:#06201d; font-weight:600; }
  main { padding:16px 20px 40px; max-width:900px; margin:0 auto; }
  .toolbar { display:flex; gap:8px; margin-bottom:12px; }
  input[type=search], textarea { width:100%; padding:10px 12px; border-radius:10px; border:1px solid var(--line); background:var(--panel); color:var(--text); font-size:14px; font-family:inherit; }
  textarea { min-height:88px; resize:vertical; }
  .card { border:1px solid var(--line); background:var(--panel); border-radius:12px; padding:12px 14px; margin-bottom:10px; }
  .card h3 { margin:0 0 4px; font-size:15px; font-weight:600; }
  .meta { font-size:12px; color:var(--muted); display:flex; gap:8px; flex-wrap:wrap; }
  .preview { margin-top:8px; color:#c9c9c9; font-size:13px; white-space:pre-wrap; overflow:hidden; max-height:80px; }
  .row { display:flex; gap:8px; margin-top:10px; flex-wrap:wrap; }
  button.action { padding:7px 12px; border-radius:8px; border:1px solid var(--line); background:#202020; color:var(--text); cursor:pointer; font-size:13px; }
  button.action.primary { background:var(--accent); border-color:var(--accent); color:#06201d; font-weight:600; }
  .locked { color:#ffb74d; font-size:12px; }
  .empty { color:var(--muted); text-align:center; padding:36px 10px; }
  dialog { border:1px solid var(--line); background:var(--panel); color:var(--text); border-radius:14px; max-width:820px; width:92vw; padding:0; }
  dialog::backdrop { background:rgba(0,0,0,.7); }
  .dlg-head { display:flex; align-items:center; gap:8px; padding:14px 16px; border-bottom:1px solid var(--line); }
  .dlg-head h2 { font-size:15px; margin:0; }
  .dlg-head button { margin-left:auto; }
  .dlg-body { padding:16px; }
  pre.content { white-space:pre-wrap; word-break:break-word; font:13px/1.5 ui-monospace, SFMono-Regular, Menlo, monospace; max-height:60vh; overflow:auto; margin:0; }
  .toast { position:fixed; left:50%; bottom:24px; transform:translateX(-50%); background:#263238; border:1px solid var(--line); padding:10px 16px; border-radius:10px; opacity:0; transition:opacity .2s; pointer-events:none; }
  .toast.show { opacity:1; }
  .chip { font-size:11px; border:1px solid var(--line); border-radius:999px; padding:1px 8px; }
</style>
</head>
<body>
<header>
  <h1>aNote LAN bridge</h1>
  <span class="status"><span id="dot" class="dot"></span><span id="statusText">connecting…</span></span>
</header>
<nav>
  <button id="tabNotes" class="active" type="button">Notes</button>
  <button id="tabClip" type="button">Clipboard</button>
</nav>
<main>
  <section id="notesView">
    <div class="toolbar"><input id="noteFilter" type="search" placeholder="Filter notes…"></div>
    <div id="notesList"></div>
  </section>
  <section id="clipView" hidden>
    <div class="card">
      <h3>Send text to aNote</h3>
      <textarea id="clipInput" placeholder="Paste or type something, it is added to the clipboard history of the phone."></textarea>
      <div class="row">
        <button id="sendClip" class="action primary" type="button">Send</button>
        <button id="refreshClip" class="action" type="button">Refresh</button>
        <button id="copyClip" class="action" type="button">Copy from phone</button>
      </div>
    </div>
    <div id="clipList"></div>
  </section>
</main>

<dialog id="noteDialog">
  <div class="dlg-head">
    <h2 id="dlgTitle">Note</h2>
    <button class="action" type="button" onclick="document.getElementById('noteDialog').close()">Close</button>
  </div>
  <div class="dlg-body">
    <pre id="dlgContent" class="content"></pre>
    <div class="row">
      <button id="dlgCopy" class="action" type="button">Copy text</button>
      <button id="dlgDownload" class="action" type="button">Download</button>
    </div>
  </div>
</dialog>

<div id="toast" class="toast"></div>

<script>
(function () {
  const token = new URLSearchParams(location.search).get('token') || '';
  const withToken = (url) => url + (url.includes('?') ? '&' : '?') + 'token=' + encodeURIComponent(token);
  const headers = token ? { 'X-Anote-Token': token } : {};
  let notes = [];
  let clipboard = [];
  let currentNoteId = null;

  const $ = (id) => document.getElementById(id);
  const esc = (value) => { const d = document.createElement('div'); d.textContent = value == null ? '' : String(value); return d.innerHTML; };

  function toast(message) {
    const element = $('toast');
    element.textContent = message;
    element.classList.add('show');
    clearTimeout(element._timer);
    element._timer = setTimeout(() => element.classList.remove('show'), 2200);
  }

  async function api(url, options) {
    const response = await fetch(withToken(url), Object.assign({ headers }, options || {}));
    if (!response.ok) throw new Error('HTTP ' + response.status);
    return response;
  }

  function setStatus(on, text) {
    $('dot').classList.toggle('on', !!on);
    $('statusText').textContent = text;
  }

  async function loadNotes() {
    try {
      const response = await api('/api/notes');
      const data = await response.json();
      notes = data.items || [];
      renderNotes();
      setStatus(true, notes.length + ' notes · local Wi-Fi');
    } catch (error) {
      setStatus(false, 'not connected');
    }
  }

  function renderNotes() {
    const filter = $('noteFilter').value.trim().toLowerCase();
    const list = $('notesList');
    const visible = notes.filter((note) => !filter || (note.title || '').toLowerCase().includes(filter) || (note.preview || '').toLowerCase().includes(filter));
    if (visible.length === 0) { list.innerHTML = '<div class="empty">No notes found.</div>'; return; }
    list.innerHTML = visible.map((note) => `
      <div class="card" data-id="${esc(note.id)}">
        <h3>${esc(note.title)}</h3>
        <div class="meta">
          <span>${esc(new Date(note.modifiedAt).toLocaleString())}</span>
          <span class="chip">${esc(note.noteType)}</span>
          ${note.pinned ? '<span class="chip">pinned</span>' : ''}
          ${(note.tags || []).map((tag) => `<span class="chip">#${esc(tag)}</span>`).join('')}
          ${note.locked ? '<span class="locked">locked safe note</span>' : ''}
        </div>
        <div class="preview">${esc(note.preview || '')}</div>
        <div class="row">
          <button class="action" data-open="${esc(note.id)}" type="button">Open</button>
          <button class="action" data-download="${esc(note.id)}" type="button">Download</button>
        </div>
      </div>`).join('');
  }

  async function openNote(id) {
    try {
      const response = await api('/api/notes/' + encodeURIComponent(id) + '?format=text');
      const text = await response.text();
      const note = notes.find((item) => item.id === id);
      currentNoteId = id;
      $('dlgTitle').textContent = note ? note.title : 'Note';
      $('dlgContent').textContent = text;
      $('noteDialog').showModal();
    } catch (error) {
      toast('Could not load the note');
    }
  }

  async function downloadNote(id) {
    try {
      const response = await api('/api/notes/' + encodeURIComponent(id) + '/download');
      const blob = await response.blob();
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      const note = notes.find((item) => item.id === id);
      link.download = ((note && note.title) || 'note') + '.txt';
      document.body.appendChild(link);
      link.click();
      setTimeout(() => { URL.revokeObjectURL(link.href); link.remove(); }, 1000);
    } catch (error) {
      toast('Could not download the note');
    }
  }

  async function loadClipboard() {
    try {
      const response = await api('/api/clipboard');
      const data = await response.json();
      clipboard = data.items || [];
      renderClipboard(clipboard);
    } catch (error) {
      $('clipList').innerHTML = '<div class="empty">Could not load the clipboard history.</div>';
    }
  }

  function renderClipboard(items) {
    const list = $('clipList');
    if (!items.length) { list.innerHTML = '<div class="empty">The clipboard history is empty.</div>'; return; }
    list.innerHTML = items.map((item) => `
      <div class="card">
        <div class="meta"><span>${esc(new Date(item.createdAt).toLocaleString())}</span>${item.favorite ? '<span class="chip">favorite</span>' : ''}</div>
        <div class="preview" style="max-height:120px">${esc(item.text)}</div>
        <div class="row">
          <button class="action" data-copy="${esc(item.id)}" type="button">Copy</button>
        </div>
      </div>`).join('');
    list.querySelectorAll('[data-copy]').forEach((button) => {
      button.addEventListener('click', async () => {
        const item = clipboard.find((entry) => entry.id === button.dataset.copy);
        if (!item) return;
        try { await navigator.clipboard.writeText(item.text); toast('Copied to this device'); }
        catch (error) { toast('Clipboard access was blocked'); }
      });
    });
  }

  async function sendClip() {
    const text = $('clipInput').value;
    if (!text.trim()) { toast('Nothing to send'); return; }
    try {
      await api('/api/clipboard', { method: 'POST', headers: Object.assign({ 'Content-Type': 'text/plain; charset=utf-8' }, headers), body: text });
      $('clipInput').value = '';
      toast('Sent to aNote');
      loadClipboard();
    } catch (error) {
      toast(error.message === 'HTTP 403' ? 'Sending is disabled in aNote settings' : 'Could not send the text');
    }
  }

  async function copyFromPhone() {
    try {
      await api('/api/clipboard', { method: 'POST', headers: Object.assign({ 'Content-Type': 'application/json' }, headers), body: JSON.stringify({ text: '' }) });
    } catch (error) { }
    toast('Use the Copy button of an item');
  }

  function connectWebSocket() {
    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    const url = scheme + '://' + location.host + '/ws' + (token ? '?token=' + encodeURIComponent(token) : '');
    let socket;
    try { socket = new WebSocket(url); } catch (error) { return; }
    socket.onopen = () => setStatus(true, 'live · local Wi-Fi');
    socket.onclose = () => { setStatus(false, 'reconnecting…'); setTimeout(connectWebSocket, 2500); };
    socket.onerror = () => { try { socket.close(); } catch (error) { } };
    socket.onmessage = (event) => {
      try {
        const message = JSON.parse(event.data);
        if (message.type === 'clipboard-changed') loadClipboard();
      } catch (error) { }
    };
  }

  $('tabNotes').addEventListener('click', () => {
    $('tabNotes').classList.add('active'); $('tabClip').classList.remove('active');
    $('notesView').hidden = false; $('clipView').hidden = true; loadNotes();
  });
  $('tabClip').addEventListener('click', () => {
    $('tabClip').classList.add('active'); $('tabNotes').classList.remove('active');
    $('notesView').hidden = true; $('clipView').hidden = false; loadClipboard();
  });
  $('noteFilter').addEventListener('input', renderNotes);
  $('sendClip').addEventListener('click', sendClip);
  $('refreshClip').addEventListener('click', loadClipboard);
  $('copyClip').addEventListener('click', copyFromPhone);
  $('notesList').addEventListener('click', (event) => {
    const openButton = event.target.closest('[data-open]');
    if (openButton) { openNote(openButton.dataset.open); return; }
    const downloadButton = event.target.closest('[data-download]');
    if (downloadButton) downloadNote(downloadButton.dataset.download);
  });
  $('dlgCopy').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText($('dlgContent').textContent); toast('Copied'); }
    catch (error) { toast('Clipboard access was blocked'); }
  });
  $('dlgDownload').addEventListener('click', () => { if (currentNoteId) downloadNote(currentNoteId); });

  loadNotes();
  loadClipboard();
  connectWebSocket();
})();
</script>
</body>
</html>
""";
    }
}
