#!/usr/bin/env python3
"""Read-only LAN viewer for aNote's encrypted WebDAV repository (Notes, Clip, Contacts)."""

from __future__ import annotations

import base64
import json
import os
import shutil
import subprocess
import threading
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urljoin, urlparse

DEFAULT_HOST = "0.0.0.0"
DEFAULT_PORT = 8787
# Public defaults stay blank. Personal values live in gitignored local.json.
DEFAULT_WEBDAV_URL = ""
DEFAULT_USERNAME = ""
DEFAULT_REMOTE_FILE = "silentnotes_repository.silentnotes"
ROOT = Path(__file__).resolve().parent
DECRYPT_DIR = ROOT / "decrypt"
LOCAL_CONFIG_PATH = ROOT / "local.json"


def load_local_config() -> dict[str, Any]:
    if not LOCAL_CONFIG_PATH.is_file():
        return {}
    try:
        data = json.loads(LOCAL_CONFIG_PATH.read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except Exception:  # noqa: BLE001
        return {}


def save_local_config(state: "AppState") -> None:
    payload = {
        "webdav_url": state.webdav_url,
        "username": state.username,
        "password": state.password,
        "remote_file": state.remote_file,
        "transfer_code": state.transfer_code,
    }
    LOCAL_CONFIG_PATH.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


class AppState:
    def __init__(self) -> None:
        local = load_local_config()
        self.lock = threading.Lock()
        self.webdav_url = str(local.get("webdav_url") or DEFAULT_WEBDAV_URL).strip()
        self.username = str(local.get("username") or DEFAULT_USERNAME).strip()
        self.password = str(local.get("password") or "").strip()
        self.remote_file = str(local.get("remote_file") or DEFAULT_REMOTE_FILE).strip()
        self.transfer_code = str(local.get("transfer_code") or "").strip()
        self.connected = False
        self.last_refresh = None
        self.last_error = None
        self.repository_id = None
        self.revision = None
        self.notes: list[dict[str, Any]] = []
        self.clipboard: list[dict[str, Any]] = []
        self.contacts: list[dict[str, Any]] = []

    def snapshot(self) -> dict[str, Any]:
        with self.lock:
            return {
                "connected": self.connected,
                "webdav_url": self.webdav_url,
                "username": self.username,
                "remote_file": self.remote_file,
                "has_password": bool(self.password),
                "has_transfer_code": bool(self.transfer_code),
                "last_refresh": self.last_refresh,
                "last_error": self.last_error,
                "repository_id": self.repository_id,
                "revision": self.revision,
                "note_count": len(self.notes),
                "clipboard_count": len(self.clipboard),
                "contact_count": len(self.contacts),
                "notes": list(self.notes),
                "clipboard": list(self.clipboard),
                "contacts": list(self.contacts),
            }


STATE = AppState()


def utc_now() -> str:
    import time

    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def join_webdav(base_url: str, remote_file: str) -> tuple[str | None, str | None]:
    """Build an absolute WebDAV file URL. Returns (url, error)."""
    base = (base_url or "").strip()
    remote = (remote_file or "").strip().lstrip("/")
    if not base:
        return None, "WebDAV URL is empty"
    parsed = urlparse(base)
    if parsed.scheme not in ("http", "https") or not parsed.netloc:
        return None, (
            f"WebDAV URL must be absolute (https://…), got {base!r}. "
            "Check the WebDAV URL field was not overwritten by browser autofill."
        )
    if "/webdav" not in (parsed.path or "").lower():
        return None, (
            f"WebDAV URL should contain /webdav/, got {base!r}. "
            "Example: https://your-host.example/webdav/"
        )
    if not remote:
        return None, "Repository file name is empty"
    # If remote_file is already an absolute URL, use it directly.
    remote_parsed = urlparse(remote)
    if remote_parsed.scheme in ("http", "https"):
        return remote, None
    joined = urljoin(base if base.endswith("/") else base + "/", remote)
    joined_parsed = urlparse(joined)
    if joined_parsed.scheme not in ("http", "https") or not joined_parsed.netloc:
        return None, f"Could not build a valid URL from {base!r} + {remote!r}"
    return joined, None


def basic_auth_header(username: str, password: str) -> str:
    token = base64.b64encode(f"{username}:{password}".encode("utf-8")).decode("ascii")
    return f"Basic {token}"


def download_repository() -> tuple[bytes | None, str | None]:
    with STATE.lock:
        if not STATE.connected or not STATE.password:
            return None, "Not connected"
        url, url_err = join_webdav(STATE.webdav_url, STATE.remote_file)
        username = STATE.username
        password = STATE.password

    if url_err:
        return None, url_err
    assert url is not None

    req = urllib.request.Request(url, method="GET")
    req.add_header("Authorization", basic_auth_header(username, password))
    req.add_header("Accept", "application/octet-stream, */*")
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.read(), None
    except urllib.error.HTTPError as exc:
        return None, format_http_error(exc, url)
    except Exception as exc:  # noqa: BLE001
        return None, str(exc)


def format_http_error(exc: urllib.error.HTTPError, url: str) -> str:
    reason = (exc.reason or "").strip()
    if exc.code == 401:
        with STATE.lock:
            user = STATE.username or "(empty)"
        return (
            f"HTTP 401 Unauthorized for {url} as user {user!r}. "
            "Keenetic rejected Basic auth — wrong WebDAV username or password "
            "(this is not the transfer code)."
        )
    if exc.code == 404:
        return f"HTTP 404 Not Found for {url} — check the repository file name."
    # Avoid dumping HTML error pages into the UI.
    body = ""
    try:
        body = exc.read().decode("utf-8", errors="replace")
    except Exception:  # noqa: BLE001
        body = ""
    if "<" in body:
        body = reason or "see server logs"
    else:
        body = (body or reason)[:160]
    return f"HTTP {exc.code}: {body}"


def find_decrypt_dll() -> Path | None:
    for cfg in ("Release", "Debug"):
        for tfm in ("net10.0", "net9.0"):
            candidate = DECRYPT_DIR / "bin" / cfg / tfm / "AnoteDecrypt.dll"
            if candidate.is_file():
                return candidate
    return None


def ensure_decrypt_built() -> str | None:
    if find_decrypt_dll() is not None:
        return None
    try:
        subprocess.run(
            ["dotnet", "build", "-c", "Release", "-v", "q"],
            cwd=str(DECRYPT_DIR),
            check=True,
            capture_output=True,
            text=True,
            timeout=180,
        )
    except Exception as exc:  # noqa: BLE001
        return f"Could not build decrypt helper: {exc}"
    if find_decrypt_dll() is None:
        return "Decrypt helper DLL not found after build"
    return None


def decrypt_repository(cipher: bytes, transfer_code: str) -> tuple[dict[str, Any] | None, str | None]:
    err = ensure_decrypt_built()
    if err:
        return None, err
    dll = find_decrypt_dll()
    assert dll is not None
    try:
        proc = subprocess.run(
            ["dotnet", str(dll), "--file", "-", "--code", transfer_code],
            input=cipher,
            capture_output=True,
            timeout=120,
        )
    except Exception as exc:  # noqa: BLE001
        return None, str(exc)

    if proc.returncode != 0:
        msg = (proc.stderr or proc.stdout or b"").decode("utf-8", errors="replace").strip()
        return None, msg or f"decrypt exited {proc.returncode}"

    try:
        payload = json.loads(proc.stdout.decode("utf-8"))
    except Exception as exc:  # noqa: BLE001
        return None, f"Invalid decrypt JSON: {exc}"
    if not isinstance(payload, dict) or not payload.get("ok"):
        return None, "Decrypt helper returned unexpected payload"
    return payload, None


def copy_to_linux_clipboard(text: str) -> str | None:
    if not text:
        return "Empty text"
    helpers = []
    if shutil.which("wl-copy"):
        helpers.append(["wl-copy", "--"])
    if shutil.which("xclip"):
        helpers.append(["xclip", "-selection", "clipboard"])
    if shutil.which("xsel"):
        helpers.append(["xsel", "--clipboard", "--input"])
    if not helpers:
        return "No wl-copy/xclip/xsel found"
    last_err = None
    for cmd in helpers:
        try:
            subprocess.run(cmd, input=text.encode("utf-8"), check=True, timeout=5)
            return None
        except Exception as exc:  # noqa: BLE001
            last_err = str(exc)
    return last_err


def refresh_now() -> str | None:
    with STATE.lock:
        if not STATE.connected:
            return "Not connected"
        code = STATE.transfer_code

    if not code:
        return "Transfer code required"

    cipher, err = download_repository()
    if err:
        with STATE.lock:
            STATE.last_error = err
            STATE.last_refresh = utc_now()
        return err
    assert cipher is not None

    payload, err = decrypt_repository(cipher, code)
    if err:
        with STATE.lock:
            STATE.last_error = err
            STATE.last_refresh = utc_now()
        return err
    assert payload is not None

    with STATE.lock:
        STATE.repository_id = payload.get("repository_id")
        STATE.revision = payload.get("revision")
        STATE.notes = payload.get("notes") or []
        STATE.clipboard = payload.get("clipboard") or []
        STATE.contacts = payload.get("contacts") or []
        STATE.last_error = None
        STATE.last_refresh = utc_now()
    return None


INDEX_HTML = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>aNote web</title>
  <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
  <style>
    :root {
      --bg: #12160f;
      --panel: #1b2318;
      --ink: #e7efe3;
      --muted: #97a891;
      --accent: #7cb87a;
      --line: #2c3a28;
      --danger: #d27a6a;
      --mono: "IBM Plex Mono", ui-monospace, monospace;
      --sans: "IBM Plex Sans", "Segoe UI", sans-serif;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      font-family: var(--sans);
      color: var(--ink);
      background:
        radial-gradient(1100px 520px at 8% -8%, #243522 0%, transparent 55%),
        radial-gradient(800px 420px at 100% 0%, #1c2a30 0%, transparent 50%),
        var(--bg);
    }
    main { max-width: 980px; margin: 0 auto; padding: 2rem 1.2rem 4rem; }
    h1 { font-size: clamp(2rem, 4.5vw, 3rem); letter-spacing: -0.04em; margin: 0 0 0.3rem; }
    .tagline { color: var(--muted); margin: 0 0 1.4rem; }
    form, .status, .tabs, .panel {
      background: color-mix(in srgb, var(--panel) 92%, black);
      border: 1px solid var(--line);
      border-radius: 14px;
      padding: 1rem 1.1rem;
      margin-bottom: 1rem;
    }
    label { display: block; font-size: 0.85rem; color: var(--muted); margin: 0.55rem 0 0.25rem; }
    input[type=text], input[type=password] {
      width: 100%;
      padding: 0.65rem 0.75rem;
      border-radius: 8px;
      border: 1px solid var(--line);
      background: #0d120c;
      color: var(--ink);
      font-family: var(--mono);
      font-size: 0.92rem;
    }
    .row { display: flex; gap: 0.7rem; flex-wrap: wrap; align-items: center; margin-top: 0.9rem; }
    button {
      border: 0; border-radius: 999px; padding: 0.55rem 1rem;
      background: var(--accent); color: #0d160c; font-weight: 650; cursor: pointer;
    }
    button.secondary { background: transparent; color: var(--ink); border: 1px solid var(--line); }
    button.tab {
      background: transparent; color: var(--muted); border: 1px solid transparent; border-radius: 999px;
    }
    button.tab.active { color: var(--ink); border-color: var(--line); background: #10160f; }
    .status dl { display: grid; grid-template-columns: 8.5rem 1fr; gap: 0.35rem 0.75rem; margin: 0; }
    .status dt { color: var(--muted); }
    .status dd { margin: 0; font-family: var(--mono); font-size: 0.9rem; word-break: break-word; }
    .err { color: var(--danger); }
    .item { border-top: 1px solid var(--line); padding: 0.85rem 0; cursor: pointer; }
    .item:first-child { border-top: 0; }
    .item:hover { color: var(--accent); }
    .meta { color: var(--muted); font-size: 0.78rem; font-family: var(--mono); margin-bottom: 0.35rem; }
    .text { white-space: pre-wrap; word-break: break-word; line-height: 1.4; }
    .empty { color: var(--muted); }
    .note-view { display: none; }
    .note-view.open { display: block; }
    .note-html {
      margin-top: 0.75rem; padding: 0.9rem; border: 1px solid var(--line); border-radius: 10px;
      background: #0e140d; max-height: 55vh; overflow: auto;
    }
    .note-html img { max-width: 100%; }
    .locked { color: var(--muted); font-style: italic; }
  </style>
</head>
<body>
  <main>
    <h1>aNote web</h1>
    <p class="tagline">Read-only LAN viewer for Notes, Clip and Contacts from WebDAV.</p>

    <form id="connect-form" autocomplete="off">
      <label for="webdav_url">WebDAV URL</label>
      <div class="row" style="margin-top:0">
        <input id="webdav_url" name="anote_webdav_url" type="text" spellcheck="false"
          style="flex:1 1 16rem" placeholder="https://your-host.example/webdav/" value="" />
        <button type="button" class="secondary" id="edit_url">Edit URL</button>
      </div>
      <label for="username">Username</label>
      <input id="username" name="anote_webdav_user" type="text" autocomplete="off" spellcheck="false" placeholder="WebDAV username" />
      <label for="password">WebDAV password (share password — not the transfer code)</label>
      <input id="password" name="anote_webdav_pass" type="password" autocomplete="off" />
      <label for="remote_file">Repository file</label>
      <input id="remote_file" name="anote_repo_file" type="text" spellcheck="false"
        value="silentnotes_repository.silentnotes" />
      <label for="transfer_code">Transfer code (16 chars from aNote; spaces OK)</label>
      <input id="transfer_code" name="anote_transfer_code" type="text" autocomplete="off" spellcheck="false" />
      <div class="row">
        <button type="submit">Connect &amp; load</button>
        <button type="button" class="secondary" id="refresh">Refresh</button>
        <button type="button" class="secondary" id="disconnect">Disconnect</button>
      </div>
    </form>

    <section class="status" id="status"></section>

    <div class="tabs row" style="margin-top:0">
      <button type="button" class="tab active" data-tab="notes">Notes</button>
      <button type="button" class="tab" data-tab="clipboard">Clip</button>
      <button type="button" class="tab" data-tab="contacts">Contacts</button>
    </div>

    <section class="panel" id="panel-notes"><div class="empty">Connect to load notes.</div></section>
    <section class="panel" id="panel-clipboard" hidden><div class="empty">No clips.</div></section>
    <section class="panel" id="panel-contacts" hidden><div class="empty">No contacts.</div></section>
  </main>
  <script>
    const $ = (id) => document.getElementById(id);
    let formSeeded = false;
    let current = null;
    let activeTab = "notes";

    async function api(path, opts) {
      const res = await fetch(path, opts);
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || res.statusText);
      return data;
    }

    function fillForm(s) {
      $("webdav_url").value = s.webdav_url || "";
      $("webdav_url").readOnly = !!s.webdav_url;
      $("username").value = s.username || "";
      $("remote_file").value = s.remote_file || "silentnotes_repository.silentnotes";
      // Never wipe secrets the user is typing; show placeholders only.
      if (!$("password").value && s.has_password) $("password").placeholder = "(saved locally)";
      if (!$("transfer_code").value && s.has_transfer_code) $("transfer_code").placeholder = "(saved locally)";
    }

    function renderStatus(s) {
      $("status").innerHTML = `
        <dl>
          <dt>connected</dt><dd>${s.connected ? "yes" : "no"}</dd>
          <dt>notes / clip / contacts</dt><dd>${s.note_count} / ${s.clipboard_count} / ${s.contact_count}</dd>
          <dt>revision</dt><dd>${escapeHtml(s.revision || "—")}</dd>
          <dt>last refresh</dt><dd>${escapeHtml(s.last_refresh || "—")}</dd>
          <dt>error</dt><dd class="${s.last_error ? "err" : ""}">${escapeHtml(s.last_error || "—")}</dd>
        </dl>`;
    }

    function setTab(name) {
      activeTab = name;
      document.querySelectorAll("button.tab").forEach((b) => {
        b.classList.toggle("active", b.dataset.tab === name);
      });
      $("panel-notes").hidden = name !== "notes";
      $("panel-clipboard").hidden = name !== "clipboard";
      $("panel-contacts").hidden = name !== "contacts";
    }

    function renderNotes(notes) {
      const panel = $("panel-notes");
      if (!notes || !notes.length) {
        panel.innerHTML = '<div class="empty">No notes.</div>';
        return;
      }
      const sorted = [...notes].sort((a, b) => (b.pinned === true) - (a.pinned === true));
      panel.innerHTML = sorted.map((n, i) => `
        <div class="item" data-idx="${i}">
          <div class="meta">${n.pinned ? "pinned · " : ""}${n.locked ? "locked · " : ""}${(n.tags || []).join(", ") || "untagged"} · ${(n.id || "").slice(0, 8)}</div>
          <div class="text">${escapeHtml(n.title || "(note)")}</div>
          <div class="note-view" id="note-view-${i}"></div>
        </div>`).join("");
      [...panel.querySelectorAll(".item")].forEach((el) => {
        const idx = Number(el.dataset.idx);
        el.addEventListener("click", (ev) => {
          if (ev.target.closest(".note-view")) return;
          const view = $("note-view-" + idx);
          const open = view.classList.toggle("open");
          if (!open) { view.innerHTML = ""; return; }
          const note = sorted[idx];
          if (note.locked) {
            view.innerHTML = '<div class="locked">This note is in a safe and stays locked in the web viewer.</div>';
          } else {
            view.innerHTML = '<div class="note-html"></div>';
            view.querySelector(".note-html").innerHTML = note.html || "";
          }
        });
      });
    }

    function renderClipboard(items) {
      const panel = $("panel-clipboard");
      if (!items || !items.length) {
        panel.innerHTML = '<div class="empty">No clipboard items in the repository.</div>';
        return;
      }
      panel.innerHTML = items.map((it, i) => `
        <div class="item" data-idx="${i}">
          <div class="meta">${it.created_utc || ""} · ${(it.id || "").slice(0, 8)}</div>
          <div class="text"></div>
        </div>`).join("");
      [...panel.querySelectorAll(".item")].forEach((el, i) => {
        el.querySelector(".text").textContent = items[i].text || "";
        el.addEventListener("click", async () => {
          await api("/api/copy", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ text: items[i].text || "" }),
          });
        });
      });
    }

    function renderContacts(items) {
      const panel = $("panel-contacts");
      if (!items || !items.length) {
        panel.innerHTML = '<div class="empty">No contacts.</div>';
        return;
      }
      panel.innerHTML = items.map((c) => `
        <div class="item" style="cursor:default">
          <div class="text">${escapeHtml(c.name || "(unnamed)")}</div>
          <div class="meta">${(c.phones || []).map(escapeHtml).join(" · ") || "no phone"}</div>
        </div>`).join("");
    }

    function escapeHtml(s) {
      return String(s).replace(/[&<>"']/g, (ch) => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
      })[ch]);
    }

    function render(s) {
      current = s;
      renderStatus(s);
      renderNotes(s.notes || []);
      renderClipboard(s.clipboard || []);
      renderContacts(s.contacts || []);
      setTab(activeTab);
    }

    async function refresh(seedForm = false) {
      const s = await api("/api/status");
      if (seedForm || !formSeeded) {
        fillForm(s);
        formSeeded = true;
      }
      render(s);
    }

    $("edit_url").addEventListener("click", () => {
      const input = $("webdav_url");
      input.readOnly = false;
      input.focus();
      input.select();
    });

    $("connect-form").addEventListener("submit", async (e) => {
      e.preventDefault();
      const webdavUrl = ($("webdav_url").value || "").trim();
      if (!/^https?:\\/\\//i.test(webdavUrl)) {
        alert("WebDAV URL must start with https:// (got: " + webdavUrl + ").");
        $("webdav_url").focus();
        return;
      }
      if (!webdavUrl.toLowerCase().includes("/webdav")) {
        alert("WebDAV URL should contain /webdav/ — current value: " + webdavUrl);
        $("webdav_url").focus();
        return;
      }
      try {
        await api("/api/connect", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            webdav_url: webdavUrl,
            username: $("username").value,
            password: $("password").value,
            remote_file: $("remote_file").value,
            transfer_code: $("transfer_code").value,
          }),
        });
        $("webdav_url").readOnly = true;
        await refresh(true);
      } catch (err) {
        alert(err.message || String(err));
        await refresh(false);
      }
    });

    $("refresh").addEventListener("click", async () => {
      await api("/api/refresh", { method: "POST" });
      await refresh(false);
    });

    $("disconnect").addEventListener("click", async () => {
      await api("/api/disconnect", { method: "POST" });
      $("password").value = "";
      $("transfer_code").value = "";
      await refresh(true);
    });

    document.querySelectorAll("button.tab").forEach((b) => {
      b.addEventListener("click", () => setTab(b.dataset.tab));
    });

    refresh(true);
  </script>
</body>
</html>
"""


class Handler(BaseHTTPRequestHandler):
    server_version = "anote-web/1.0"

    def log_message(self, fmt: str, *args: Any) -> None:
        print(f"[anote-web] {self.address_string()} - {fmt % args}")

    def _send(self, code: int, body: bytes, content_type: str) -> None:
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _json(self, code: int, obj: Any) -> None:
        data = json.dumps(obj).encode("utf-8")
        self._send(code, data, "application/json; charset=utf-8")

    def _read_json(self) -> dict[str, Any]:
        length = int(self.headers.get("Content-Length") or "0")
        raw = self.rfile.read(length) if length else b"{}"
        try:
            obj = json.loads(raw.decode("utf-8"))
        except Exception:  # noqa: BLE001
            return {}
        return obj if isinstance(obj, dict) else {}

    def do_GET(self) -> None:  # noqa: N802
        if self.path in ("/", "/index.html"):
            self._send(200, INDEX_HTML.encode("utf-8"), "text/html; charset=utf-8")
            return
        if self.path in ("/favicon.svg", "/favicon.ico"):
            # Square split into two horizontal bands: green (top) and yellow (bottom).
            svg = (
                '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">'
                '<rect width="32" height="16" fill="#7cb87a"/>'
                '<rect y="16" width="32" height="16" fill="#e6c84a"/>'
                "</svg>"
            )
            self._send(200, svg.encode("utf-8"), "image/svg+xml")
            return
        if self.path == "/api/status":
            self._json(200, STATE.snapshot())
            return
        self._json(404, {"error": "not found"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path == "/api/connect":
            body = self._read_json()
            webdav_url = (body.get("webdav_url") or "").strip() or STATE.webdav_url
            remote_file = (body.get("remote_file") or "").strip() or STATE.remote_file or DEFAULT_REMOTE_FILE
            username = (body.get("username") or "").strip() or STATE.username
            _, url_err = join_webdav(webdav_url, remote_file)
            if url_err:
                with STATE.lock:
                    STATE.last_error = url_err
                self._json(400, {"error": url_err, **STATE.snapshot()})
                return

            with STATE.lock:
                STATE.webdav_url = webdav_url
                STATE.username = username
                password = body.get("password")
                if isinstance(password, str) and password.strip() != "":
                    STATE.password = password.strip()
                code = body.get("transfer_code")
                if isinstance(code, str) and code.strip() != "":
                    STATE.transfer_code = code.strip().replace(" ", "").replace("-", "")
                STATE.remote_file = remote_file
                has_password = bool(STATE.password)
                has_code = bool(STATE.transfer_code)
                has_user = bool(STATE.username)
                STATE.connected = has_password and has_code and has_user
                if not has_user:
                    STATE.last_error = "WebDAV username required"
                elif not has_password:
                    STATE.last_error = "WebDAV password required (share password, not the transfer code)"
                elif not has_code:
                    STATE.last_error = "Transfer code required (from aNote → show transfer code)"
                else:
                    STATE.last_error = None
            if not STATE.connected:
                self._json(400, {"error": STATE.snapshot()["last_error"], **STATE.snapshot()})
                return
            err = refresh_now()
            if err:
                self._json(400, {"error": err, **STATE.snapshot()})
                return
            try:
                save_local_config(STATE)
            except Exception as exc:  # noqa: BLE001
                with STATE.lock:
                    STATE.last_error = f"Loaded OK, but could not save local.json: {exc}"
            self._json(200, STATE.snapshot())
            return

        if self.path == "/api/refresh":
            err = refresh_now()
            if err:
                self._json(400, {"error": err, **STATE.snapshot()})
                return
            self._json(200, STATE.snapshot())
            return

        if self.path == "/api/disconnect":
            with STATE.lock:
                STATE.connected = False
                STATE.password = ""
                STATE.transfer_code = ""
                STATE.notes = []
                STATE.clipboard = []
                STATE.contacts = []
                STATE.last_error = None
                STATE.repository_id = None
                STATE.revision = None
            self._json(200, STATE.snapshot())
            return

        if self.path == "/api/copy":
            body = self._read_json()
            text = body.get("text") if isinstance(body.get("text"), str) else ""
            err = copy_to_linux_clipboard(text)
            if err:
                self._json(500, {"error": err})
                return
            self._json(200, {"ok": True})
            return

        self._json(404, {"error": "not found"})


def main() -> None:
    host = os.environ.get("ANOTE_WEB_HOST", DEFAULT_HOST)
    port = int(os.environ.get("ANOTE_WEB_PORT", str(DEFAULT_PORT)))
    build_err = ensure_decrypt_built()
    if build_err:
        print(f"[anote-web] warning: {build_err}")
    httpd = ThreadingHTTPServer((host, port), Handler)
    print(f"anote-web listening on http://{host}:{port}/")
    if STATE.webdav_url or STATE.username:
        print(f"loaded local.json for user={STATE.username!r} url={STATE.webdav_url!r}")
    else:
        print("no local.json yet — enter WebDAV settings in the browser (saved to gitignored local.json)")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nanote-web stopped")


if __name__ == "__main__":
    main()
