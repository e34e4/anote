#!/usr/bin/env python3
"""Read-only LAN clipmon: poll aNote WebDAV clipboard_sync.json and serve a browser UI."""

from __future__ import annotations

import base64
import hashlib
import json
import os
import shutil
import subprocess
import threading
import time
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
DEFAULT_REMOTE_PATH = "anote/clipboard_sync.json"
POLL_INTERVAL_SEC = 15.0
ROOT = Path(__file__).resolve().parent
LOCAL_CONFIG_PATH = ROOT / "local.json"


def load_local_config() -> dict[str, Any]:
    if not LOCAL_CONFIG_PATH.is_file():
        return {}
    try:
        data = json.loads(LOCAL_CONFIG_PATH.read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except Exception:  # noqa: BLE001
        return {}


def save_local_config(state: "ClipmonState") -> None:
    payload = {
        "webdav_url": state.webdav_url,
        "username": state.username,
        "password": state.password,
        "remote_path": state.remote_path,
        "auto_copy": state.auto_copy,
    }
    LOCAL_CONFIG_PATH.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


class ClipmonState:
    def __init__(self) -> None:
        local = load_local_config()
        self.lock = threading.Lock()
        self.webdav_url = str(local.get("webdav_url") or DEFAULT_WEBDAV_URL).strip()
        self.username = str(local.get("username") or DEFAULT_USERNAME).strip()
        self.password = str(local.get("password") or "").strip()
        self.remote_path = str(local.get("remote_path") or DEFAULT_REMOTE_PATH).strip()
        self.connected = False
        self.items: list[dict[str, Any]] = []
        self.updated_utc: str | None = None
        self.last_poll_utc: str | None = None
        self.last_error: str | None = None
        self.content_hash: str | None = None
        self.last_applied_id: str | None = None
        self.auto_copy = bool(local.get("auto_copy", True))

    def snapshot(self) -> dict[str, Any]:
        with self.lock:
            return {
                "connected": self.connected,
                "webdav_url": self.webdav_url,
                "username": self.username,
                "remote_path": self.remote_path,
                "has_password": bool(self.password),
                "updated_utc": self.updated_utc,
                "last_poll_utc": self.last_poll_utc,
                "last_error": self.last_error,
                "auto_copy": self.auto_copy,
                "item_count": len(self.items),
                "items": list(self.items),
            }


STATE = ClipmonState()


def utc_now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def join_webdav(base_url: str, remote_path: str) -> str:
    base = base_url if base_url.endswith("/") else base_url + "/"
    path = remote_path.lstrip("/")
    return urljoin(base, path)


def basic_auth_header(username: str, password: str) -> str:
    token = base64.b64encode(f"{username}:{password}".encode("utf-8")).decode("ascii")
    return f"Basic {token}"


def fetch_sync_file() -> tuple[bytes | None, str | None]:
    with STATE.lock:
        if not STATE.connected or not STATE.password:
            return None, "Not connected"
        url = join_webdav(STATE.webdav_url, STATE.remote_path)
        username = STATE.username
        password = STATE.password

    req = urllib.request.Request(url, method="GET")
    req.add_header("Authorization", basic_auth_header(username, password))
    req.add_header("Accept", "application/json, text/plain, */*")
    try:
        with urllib.request.urlopen(req, timeout=20) as resp:
            return resp.read(), None
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        return None, f"HTTP {exc.code}: {body[:200] or exc.reason}"
    except Exception as exc:  # noqa: BLE001
        return None, str(exc)


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


def apply_items(payload: dict[str, Any], content_hash: str) -> None:
    items = payload.get("items") or []
    if not isinstance(items, list):
        items = []
    clean: list[dict[str, Any]] = []
    for raw in items:
        if not isinstance(raw, dict):
            continue
        text = raw.get("text")
        if not isinstance(text, str) or not text:
            continue
        clean.append(
            {
                "id": str(raw.get("id") or ""),
                "created_utc": raw.get("created_utc"),
                "modified_utc": raw.get("modified_utc"),
                "kind": raw.get("kind") or "text",
                "hash": raw.get("hash"),
                "text": text,
            }
        )

    newest_id = clean[0]["id"] if clean else None
    should_copy = False
    with STATE.lock:
        STATE.items = clean
        STATE.updated_utc = payload.get("updated_utc")
        STATE.content_hash = content_hash
        STATE.last_error = None
        if STATE.auto_copy and newest_id and newest_id != STATE.last_applied_id:
            should_copy = True
            STATE.last_applied_id = newest_id
            text_to_copy = clean[0]["text"]
        else:
            text_to_copy = None

    if should_copy and text_to_copy is not None:
        err = copy_to_linux_clipboard(text_to_copy)
        if err:
            with STATE.lock:
                STATE.last_error = f"Clipboard apply failed: {err}"


def poll_once() -> None:
    with STATE.lock:
        connected = STATE.connected and bool(STATE.password)
    if not connected:
        return

    data, err = fetch_sync_file()
    with STATE.lock:
        STATE.last_poll_utc = utc_now()
        if err:
            STATE.last_error = err
            return

    assert data is not None
    digest = hashlib.sha256(data).hexdigest()
    with STATE.lock:
        unchanged = digest == STATE.content_hash
    if unchanged:
        return

    try:
        payload = json.loads(data.decode("utf-8"))
        if not isinstance(payload, dict):
            raise ValueError("Root JSON must be an object")
    except Exception as exc:  # noqa: BLE001
        with STATE.lock:
            STATE.last_error = f"Invalid JSON: {exc}"
        return

    apply_items(payload, digest)


def poll_loop() -> None:
    while True:
        try:
            poll_once()
        except Exception as exc:  # noqa: BLE001
            with STATE.lock:
                STATE.last_error = str(exc)
        time.sleep(POLL_INTERVAL_SEC)


INDEX_HTML = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>clipmon</title>
  <style>
    :root {
      --bg: #10140f;
      --panel: #1a2218;
      --ink: #e8f0e4;
      --muted: #9bb094;
      --accent: #7cb87a;
      --line: #2c3a28;
      --danger: #d27a6a;
      --mono: "IBM Plex Mono", "JetBrains Mono", ui-monospace, monospace;
      --sans: "IBM Plex Sans", "Segoe UI", sans-serif;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      font-family: var(--sans);
      color: var(--ink);
      background:
        radial-gradient(1200px 600px at 10% -10%, #243522 0%, transparent 55%),
        radial-gradient(900px 500px at 100% 0%, #1c2a30 0%, transparent 50%),
        var(--bg);
    }
    main {
      max-width: 920px;
      margin: 0 auto;
      padding: 2rem 1.25rem 4rem;
    }
    h1 {
      font-size: clamp(2.2rem, 5vw, 3.4rem);
      letter-spacing: -0.04em;
      margin: 0 0 0.35rem;
      font-weight: 650;
    }
    .tagline { color: var(--muted); margin: 0 0 1.5rem; }
    form, .status, .list {
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
    .row { display: flex; gap: 0.75rem; flex-wrap: wrap; align-items: center; margin-top: 0.9rem; }
    button {
      border: 0;
      border-radius: 999px;
      padding: 0.55rem 1rem;
      background: var(--accent);
      color: #0d160c;
      font-weight: 650;
      cursor: pointer;
    }
    button.secondary { background: transparent; color: var(--ink); border: 1px solid var(--line); }
    .status dl { display: grid; grid-template-columns: 8rem 1fr; gap: 0.35rem 0.75rem; margin: 0; }
    .status dt { color: var(--muted); }
    .status dd { margin: 0; font-family: var(--mono); font-size: 0.9rem; word-break: break-word; }
    .err { color: var(--danger); }
    .item {
      border-top: 1px solid var(--line);
      padding: 0.85rem 0;
      cursor: pointer;
    }
    .item:first-child { border-top: 0; }
    .item:hover { color: var(--accent); }
    .meta { color: var(--muted); font-size: 0.78rem; font-family: var(--mono); margin-bottom: 0.35rem; }
    .text { white-space: pre-wrap; word-break: break-word; line-height: 1.4; }
    .empty { color: var(--muted); }
  </style>
</head>
<body>
  <main>
    <h1>clipmon</h1>
    <p class="tagline">Read-only LAN reader for aNote’s WebDAV clipboard sync file.</p>

    <form id="connect-form">
      <label for="webdav_url">WebDAV URL</label>
      <input id="webdav_url" name="webdav_url" type="text" autocomplete="off" />
      <label for="username">Username</label>
      <input id="username" name="username" type="text" autocomplete="username" />
      <label for="password">Password</label>
      <input id="password" name="password" type="password" autocomplete="current-password" />
      <label for="remote_path">Remote path</label>
      <input id="remote_path" name="remote_path" type="text" autocomplete="off" />
      <div class="row">
        <button type="submit">Connect</button>
        <button type="button" class="secondary" id="disconnect">Disconnect</button>
        <button type="button" class="secondary" id="refresh">Refresh</button>
        <label style="display:flex;gap:0.4rem;align-items:center;margin:0;color:var(--ink)">
          <input type="checkbox" id="auto_copy" checked /> Auto-copy newest to Linux clipboard
        </label>
      </div>
    </form>

    <section class="status" id="status"></section>
    <section class="list" id="list"><div class="empty">No items yet.</div></section>
  </main>
  <script>
    const $ = (id) => document.getElementById(id);
    let formSeeded = false;

    async function api(path, opts) {
      const res = await fetch(path, opts);
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || res.statusText);
      return data;
    }

    function fillForm(s) {
      $("webdav_url").value = s.webdav_url || "";
      $("username").value = s.username || "";
      $("remote_path").value = s.remote_path || "";
      $("auto_copy").checked = !!s.auto_copy;
      // Never clear a password the user is typing; only clear after disconnect.
      if (!s.has_password && !document.activeElement?.closest("#password"))
        $("password").value = "";
    }

    function render(s) {
      $("status").innerHTML = `
        <dl>
          <dt>connected</dt><dd>${s.connected ? "yes" : "no"}</dd>
          <dt>items</dt><dd>${s.item_count}</dd>
          <dt>updated</dt><dd>${s.updated_utc || "—"}</dd>
          <dt>last poll</dt><dd>${s.last_poll_utc || "—"}</dd>
          <dt>error</dt><dd class="${s.last_error ? "err" : ""}">${s.last_error || "—"}</dd>
        </dl>`;
      const list = $("list");
      if (!s.items || !s.items.length) {
        list.innerHTML = '<div class="empty">No items yet.</div>';
        return;
      }
      list.innerHTML = s.items.map((it) => `
        <div class="item" data-id="${it.id}">
          <div class="meta">${it.created_utc || ""} · ${it.id.slice(0, 8)}</div>
          <div class="text"></div>
        </div>`).join("");
      [...list.querySelectorAll(".item")].forEach((el, i) => {
        el.querySelector(".text").textContent = s.items[i].text;
        el.addEventListener("click", async () => {
          await api("/api/copy", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ id: s.items[i].id }),
          });
        });
      });
    }

    async function refresh(seedForm = false) {
      const s = await api("/api/status");
      if (seedForm || !formSeeded) {
        fillForm(s);
        formSeeded = true;
      } else {
        $("auto_copy").checked = !!s.auto_copy;
      }
      render(s);
    }

    $("connect-form").addEventListener("submit", async (e) => {
      e.preventDefault();
      await api("/api/connect", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          webdav_url: $("webdav_url").value,
          username: $("username").value,
          password: $("password").value,
          remote_path: $("remote_path").value,
          auto_copy: $("auto_copy").checked,
        }),
      });
      await refresh(true);
    });

    $("disconnect").addEventListener("click", async () => {
      await api("/api/disconnect", { method: "POST" });
      $("password").value = "";
      await refresh(true);
    });

    $("refresh").addEventListener("click", async () => {
      await api("/api/poll", { method: "POST" });
      await refresh(false);
    });

    $("auto_copy").addEventListener("change", async () => {
      await api("/api/options", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ auto_copy: $("auto_copy").checked }),
      });
    });

    refresh(true);
  </script>
</body>
</html>
"""


class Handler(BaseHTTPRequestHandler):
    server_version = "clipmon/1.0"

    def log_message(self, fmt: str, *args: Any) -> None:
        print(f"[clipmon] {self.address_string()} - {fmt % args}")

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
        if self.path == "/api/status":
            self._json(200, STATE.snapshot())
            return
        if self.path == "/api/items":
            snap = STATE.snapshot()
            self._json(200, {"items": snap["items"]})
            return
        self._json(404, {"error": "not found"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path == "/api/connect":
            body = self._read_json()
            with STATE.lock:
                STATE.webdav_url = (body.get("webdav_url") or STATE.webdav_url or DEFAULT_WEBDAV_URL).strip()
                STATE.username = (body.get("username") or STATE.username or DEFAULT_USERNAME).strip()
                password = body.get("password")
                if isinstance(password, str) and password.strip() != "":
                    STATE.password = password.strip()
                STATE.remote_path = (body.get("remote_path") or STATE.remote_path or DEFAULT_REMOTE_PATH).strip()
                if "auto_copy" in body:
                    STATE.auto_copy = bool(body.get("auto_copy"))
                STATE.connected = bool(STATE.password) and bool(STATE.webdav_url) and bool(STATE.username)
                STATE.last_error = None if STATE.connected else "WebDAV URL, username, and password required"
                STATE.content_hash = None
            if STATE.connected:
                try:
                    save_local_config(STATE)
                except Exception as exc:  # noqa: BLE001
                    with STATE.lock:
                        STATE.last_error = f"Connected, but could not save local.json: {exc}"
                threading.Thread(target=poll_once, daemon=True).start()
            self._json(200, STATE.snapshot())
            return

        if self.path == "/api/disconnect":
            with STATE.lock:
                STATE.connected = False
                STATE.password = ""
                STATE.last_error = None
            self._json(200, STATE.snapshot())
            return

        if self.path == "/api/options":
            body = self._read_json()
            with STATE.lock:
                if "auto_copy" in body:
                    STATE.auto_copy = bool(body.get("auto_copy"))
            self._json(200, STATE.snapshot())
            return

        if self.path == "/api/poll":
            poll_once()
            self._json(200, STATE.snapshot())
            return

        if self.path == "/api/copy":
            body = self._read_json()
            item_id = str(body.get("id") or "")
            text = None
            with STATE.lock:
                for item in STATE.items:
                    if item.get("id") == item_id:
                        text = item.get("text")
                        break
                if text is None and STATE.items:
                    text = STATE.items[0].get("text")
            err = copy_to_linux_clipboard(text or "")
            if err:
                self._json(500, {"error": err})
                return
            self._json(200, {"ok": True})
            return

        self._json(404, {"error": "not found"})


def main() -> None:
    host = os.environ.get("CLIPMON_HOST", DEFAULT_HOST)
    port = int(os.environ.get("CLIPMON_PORT", str(DEFAULT_PORT)))
    threading.Thread(target=poll_loop, daemon=True).start()
    httpd = ThreadingHTTPServer((host, port), Handler)
    print(f"clipmon listening on http://{host}:{port}/")
    if STATE.webdav_url or STATE.username:
        print(f"loaded local.json for user={STATE.username!r} url={STATE.webdav_url!r}")
    else:
        print("no local.json yet — enter WebDAV settings in the browser (saved to gitignored local.json)")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nclipmon stopped")


if __name__ == "__main__":
    main()
