# aNote web

Read-only LAN browser for Notes, Clip, and Contacts from the encrypted aNote/SilentNotes WebDAV repository.

## What it does

1. Downloads `silentnotes_repository.silentnotes` from WebDAV (GET only)
2. Decrypts with your **transfer code** (SilentNotes `Cryptor`)
3. Shows Notes / Clip / Contacts in the browser

Safe-locked notes stay locked. Nothing is uploaded or modified on WebDAV.

## Local settings (not published)

Copy the example and fill in your values:

```bash
cp tools/anote-web/local.example.json tools/anote-web/local.json
```

`local.json` is gitignored. The UI also writes it after a successful Connect (WebDAV URL, username, password, transfer code).

## Run

```bash
./tools/anote-web/run.sh
```

Open `http://<this-pc-lan-ip>:8787/`.

Enter WebDAV URL / username / password and the aNote transfer code, then **Connect & load**. Use **Refresh** to pull again (no auto-reload).

Env:

- `ANOTE_WEB_HOST` (default `0.0.0.0`)
- `ANOTE_WEB_PORT` (default `8787`)
