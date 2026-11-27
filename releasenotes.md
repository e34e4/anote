# aNote release notes

Collected from [GitHub Releases](https://github.com/e34e4/anote/releases).

## Version 1.3.135 (v1.3.135)

Date: 2026-11-27 · Tag: `v1.3.135` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.135)

• Abro bookmarks import now merges like linqs: adds new URLs and replaces an existing title when the imported name for the same URL is shorter.
• Blazor sleep/resume render-batch desync is recovered quietly and no longer emails Crash/Error reports.

---

## Version 1.3.132 (v1.3.132)

Date: 2026-11-26 · Tag: `v1.3.132` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.132)

SMS notifications now open in a movable/resizable floating window with an Open in aNote button targeting that conversation; quick-menu tab/page navigation works reliably on resume; aNote log classifies errors, tracks email-sent status, and automatically emails unique errors (with device name) when a mail account is configured.

---

## Version 1.3.131 (v1.3.131)

Date: 2026-11-25 · Tag: `v1.3.131` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.131)

Fix Notes list stuck on InvalidRepository/widget hub note (XmlAttribute DateTime? on bookmarks), stop caching or mutating InvalidRepository, and harden Clip tab rendering so large clipboard histories no longer SIGSEGV.

---

## Version 1.3.130 (v1.3.130)

Date: 2026-11-25 · Tag: `v1.3.130` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.130)

Fix Notes overview stuck on the widget hub note: auto-clear filters that hide ordinary notes, stop widget/clipboard helpers from clearing the live repository cache, and separate the focus-log note id from the widget note.

---

## Version 1.3.129 (v1.3.129)

Date: 2026-11-25 · Tag: `v1.3.129` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.129)

• Fix Notes list showing only the widget note: hide internal __anote_*__ tags from the filter tree and clear accidental system-tag / metadata filters on load.

---

## Version 1.3.128 (v1.3.128)

Date: 2026-11-25 · Tag: `v1.3.128` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.128)

• Ambient Android: anote:// deep links, launcher shortcuts, clipboard widget, floating mini-pad.
• Notes: daily journals, status/priority/due + list/table/board views, focus timer logging.
• Booky: auto strip tracking params, dead-link checker; Calc custom rates; Rec voice memos.
• Camera: document scan OCR with perspective crop; in-app image viewer with pinch zoom.
• File/Player/Reader: sort by date/name/size, sticky path bars, rename, two-line names.
• Contacts CRM (local notes, follow-ups, interaction log) and Settings automation rules (SMS/alarm/RSS → actions).

---

## Version 1.3.127 (v1.3.127)

Date: 2026-11-22 · Tag: `v1.3.127` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.127)

• AI History: open all previous AI chats from the notes ⋮ menu and continue any chat with full context.
• Home-screen widget: black pad with green text synced to a pinned “widget” note; tap to type, then Open in aNote.

---

## Version 1.3.126 (v1.3.126)

Date: 2026-11-17 · Tag: `v1.3.126` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.126)

• Abro AI: picture questions no longer fail with "read operation timed out" — replies are streamed, so long vision answers wait instead of timing out at 2 minutes.
• Attached pictures are downscaled before sending, so uploads are much faster and use less data.
• The answer now appears while it is being written, with an elapsed-seconds counter.
• Failed requests are retried automatically, and a cut-off answer keeps the text received so far.

---

## Version 1.3.125 (v1.3.125)

Date: 2026-11-15 · Tag: `v1.3.125` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.125)

• Global search across notes, clips, bookmarks, mail, Telegram, RSS and password logins, reachable from the overflow menu, the quick menu and a gesture.
• Encrypted file attachments in notes (PDF, audio, archives, …) with open and share.
• Player queue/playlists with auto-advance, an ongoing media notification with background playback, playback speed and sidecar subtitles for videos.
• Explicit AI actions (summarize, translate, rewrite) for selected text, whole notes, emails, RSS articles and Reader documents/PDFs.
• Device-to-device sync over the local Wi-Fi, without FTP/WebDAV or cloud accounts; the LAN bridge now also serves the encrypted repository container.
• Abro: reader mode, AI page translation and cross-device tab sync over WebDAV.

---

## Version 1.3.124 (v1.3.124)

Date: 2026-11-13 · Tag: `v1.3.124` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.124)

• Split release APK by architecture (arm64-v8a, x86_64) for significantly reduced download size.

---

## Version 1.3.123 (v1.3.123)

Date: 2026-11-13 · Tag: `v1.3.123` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.123)

_No release notes._

---

## Version 1.3.117 (v1.3.117)

Date: 2026-11-12 · Tag: `v1.3.117` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.117)

• Reddit: run Chromium media fetch on UI thread (fixes JavaBridge crash)
• Prefer DASH progressive over CMAF for v.redd.it

---

## Version 1.3.116 (v1.3.116)

Date: 2026-11-12 · Tag: `v1.3.116` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.116)

• Build fix for SOCKS helper logging
• Reddit: Chromium fetch CORS + SOCKS auth forwarder

---

## Version 1.3.114 (v1.3.114)

Date: 2026-11-12 · Tag: `v1.3.114` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.114)

• Log page: newest entries first
• Viewer: URL history button + Settings clear
• Reddit: apply SOCKS to WebView resolve + force-play media scan

---

## Version 1.3.113 (v1.3.113)

Date: 2026-11-12 · Tag: `v1.3.113` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.113)

• Reddit: only accept media after the target post loads (fixes wrong/same feed video)

---

## Version 1.3.112 (v1.3.112)

Date: 2026-11-12 · Tag: `v1.3.112` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.112)

• Reddit: download v.redd.it via Chromium fetch (fixes 403 from Java/curl)
• Prefer packaged-media.redd.it when available

---

## Version 1.3.111 (v1.3.111)

Date: 2026-11-12 · Tag: `v1.3.111` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.111)

• Floating video: TextureView playback (fixes black screen) + play/pause/prev/next
• Reddit: warmer WebView resolve with JSON bridge and media intercept

---

## Version 1.3.110 (v1.3.110)

Date: 2026-11-12 · Tag: `v1.3.110` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.110)

• VK: download CDN via Java HTTP (avoids yt-dlp DNS failure on okcdn)
• Reddit: resolve media from post page WebView (JSON was login-walled)
• Clipboard toast: top teal banner via accessibility WindowManager

---

## Version 1.3.109 (v1.3.109)

Date: 2026-11-12 · Tag: `v1.3.109` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.109)

• CI: prefetch youtubedl-android AARs (fix XA4236 Maven 404 flakiness)
• VK short CDN filenames + Reddit WebView media resolve (from 1.3.108)

---

## Version 1.3.107 (v1.3.107)

Date: 2026-11-12 · Tag: `v1.3.107` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.107)

• Fix VK Video resolve via Android HTTP (bypass sinkholed vk.com)
• Reddit: warm WebView session cookies before yt-dlp; cookies file without UTF-8 BOM
• Clipboard toast: top teal overlay / MudBlazor snackbar (not default bottom Toast)

---

## Version 1.3.104 (v1.3.104)

Date: 2026-11-12 · Tag: `v1.3.104` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.104)

• yt-dlp: resolve VK Video via vkvideo.ru (bypass sinkholed vk.com)
• yt-dlp: rewrite Vimeo links to player.vimeo.com; attach Abro cookies for Reddit/Vimeo
• yt-dlp: use SOCKS5 proxy when enabled; clearer auth/network hints
• Clipboard paste toast moved to top and colorized (teal)

---

## Version 1.3.102 (v1.3.102)

Date: 2026-11-12 · Tag: `v1.3.102` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.102)

• Fix Log page Razor build (@code/@page clashes) and Warn(Exception) overload
• Same unified anote.log viewer from 1.3.101

---

## Version 1.3.100 (v1.3.100)

Date: 2026-11-11 · Tag: `v1.3.100` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.100)

• Do not rewrite vkvideo.ru links to vk.com (vk.com is DNS-blocked on some networks)
• Fixes VK downloads failing with Connection refused while YouTube worked

---

## Version 1.3.99 (v1.3.99)

Date: 2026-11-11 · Tag: `v1.3.99` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.99)

• Fix yt-dlp Python extract: preserve lib symlinks (zlib was broken)
• Also unpack ffmpeg native libs used by yt-dlp merges
• First download after install re-extracts the runtime automatically

---

## Version 1.3.98 (v1.3.98)

Date: 2026-11-11 · Tag: `v1.3.98` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.98)

• Fix yt-dlp on Android: use bundled Python (Linux ELFs cannot run)
• Auto-replace broken Linux yt-dlp downloads with the Python script
• Requires reinstall of this build (includes Android Python runtime)

---

## Version 1.3.97 (v1.3.97)

Date: 2026-11-11 · Tag: `v1.3.97` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.97)

• Fix yt-dlp/ffmpeg Permission denied on Android 10+ (run via system linker)
• Downloads from Player / URLCheck should work again on modern devices

---

## Version 1.3.96 (v1.3.96)

Date: 2026-11-11 · Tag: `v1.3.96` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.96)

• Player: Play URL asks Play vs yt-dlp download (default video 360p)
• URLCheck: yt-dlp download with the same quality dialog
• Downloads go to /sdcard/anote/down; errors logged under /sdcard/anote/log

---

## Version 1.3.95 (v1.3.95)

Date: 2026-11-11 · Tag: `v1.3.95` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.95)

• Toasts: smaller, near the top of the screen
• Toast colors by severity (teal / blue / green / amber / red)

---

## Version 1.3.94 (v1.3.94)

Date: 2026-11-11 · Tag: `v1.3.94` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.94)

• Fix build: AnotePaths.File renamed to FileDir (no longer shadows System.IO.File)
• File settings: Connect OAuth for Dropbox, Google Drive, and OneDrive (tokens under /sdcard/anote/file)

---

## Version 1.3.92 (v1.3.92)

Date: 2026-11-11 · Tag: `v1.3.92` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.92)

• Telegram: stop auto-sending login codes when a session is dead
• Telegram: never fall back to Console.ReadLine for verification codes on Android
• Telegram: clear message to log in again; re-login refreshes the existing account

---

## Version 1.3.91 (v1.3.91)

Date: 2026-11-11 · Tag: `v1.3.91` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.91)

• Telegram: fix Arg_PlatformNotSupported on chat list (install safe logger at process start)
• Telegram: keep sessions in private app storage and migrate from /sdcard/anote/tg
• Telegram: resume with user_id so opening the tab does not send a new login code

---

## Version 1.3.90 (v1.3.90)

Date: 2026-11-11 · Tag: `v1.3.90` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.90)

• Camera: action buttons in a 3×3 grid; keep one crop control (re-encode uses Tune)
• AI sync: use same WebDAV credentials as quick-save; create missing folders; migrate legacy store
• AI chat: dropdown to pick provider for the next message
• Telegram: fix Arg_PlatformNotSupported when loading the chat list

---

## Version 1.3.89 (v1.3.89)

Date: 2026-11-11 · Tag: `v1.3.89` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.89)

• Bundle only arm64 ffmpeg/yt-dlp in the APK to reduce size (~80 MB tools, not ~200 MB)
• Other ABIs can still download via Settings → Update ffmpeg / yt-dlp

---

## Version 1.3.88 (v1.3.88)

Date: 2026-11-11 · Tag: `v1.3.88` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.88)

• Ship ffmpeg and yt-dlp with the APK; install under /sdcard/anote and app storage
• Settings: Update ffmpeg / Update yt-dlp buttons to fetch latest builds

---

## Version 1.3.87 (v1.3.87)

Date: 2026-11-11 · Tag: `v1.3.87` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.87)

• Camera can record video (720p/1080p, bitrate, optional sound)
• Floating image/video viewer; viewer Crop renamed to Edit with video cut/convert
• All app data under /sdcard/anote (pic, down, tg, abro, ffmpeg, yt-dlp)
• Extra cloud locations (WebDAV/Dropbox/Drive/OneDrive/Mega) in File tab
• SOCKS5 proxy settings with optional AI routing; narrower settings sliders
• Multi-select delete in Player/Reader/Mail/RSS; scroll-jump back arrow
• TG message formatting renders bold/italic as HTML; note EOL markers hidden

---

## Version 1.3.86 (v1.3.86)

Date: 2026-11-10 · Tag: `v1.3.86` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.86)

• TG photos open in a draggable floating overlay like the quick menu
• Drag the header or picture, resize from the bottom bar; Crop still available

---

## Version 1.3.85 (v1.3.85)

Date: 2026-11-10 · Tag: `v1.3.85` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.85)

• Fix TG photo floating viewer: open on UI thread after a short post-save delay

---

## Version 1.3.83 (v1.3.83)

Date: 2026-11-10 · Tag: `v1.3.83` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.83)

• TG chat photos open in a floating image viewer after download
• Crop from that viewer opens the image editor in the same floating style

---

## Version 1.3.82 (v1.3.82)

Date: 2026-11-10 · Tag: `v1.3.82` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.82)

_No release notes._

---

## Version 1.3.81 (v1.3.81)

Date: 2026-11-10 · Tag: `v1.3.81` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.81)

_No release notes._

---

## Version 1.3.80 (v1.3.80)

Date: 2026-11-10 · Tag: `v1.3.80` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.80)

• Finish Android ScrollView/HorizontalScrollView aliases in image editor
• Crop/blur handles, text/emoji preview, stronger blur, nav-bar inset

---

## Version 1.3.79 (v1.3.79)

Date: 2026-11-10 · Tag: `v1.3.79` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.79)

• Actually use Android ScrollView alias in image editor build
• Crop/blur handles, text/emoji preview, stronger blur, nav-bar inset

---

## Version 1.3.76 (v1.3.76)

Date: 2026-11-10 · Tag: `v1.3.76` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.76)

• Fix Abro Android.OS namespace clash in bookmarks new-tab handler
• Image editor PointF/RectF build fix; Abro bookmarks linqs-style UI

---

## Version 1.3.72 (v1.3.72)

Date: 2026-11-10 · Tag: `v1.3.72` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.72)

_No release notes._

---

## Version 1.3.71 (v1.3.71)

Date: 2026-11-10 · Tag: `v1.3.71` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.71)

_No release notes._

---

## Version 1.3.70 (v1.3.70)

Date: 2026-11-09 · Tag: `v1.3.70` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.70)

• System: Logcat tab dumps buffer to file, search, wrap, copy, severity colors
• Abro: Block images mode with same-size placeholders + link
• Abro: SOCKS5 proxy add/edit/remove/enable in ⋮ → Proxy
• TG: Search channels/groups and Premium posts search in ⋮ menu
• TG: List accounts directly in ⋮ for faster switching

---

## Version 1.3.69 (v1.3.69)

Date: 2026-11-07 · Tag: `v1.3.69` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.69)

_No release notes._

---

## Version 1.3.68 (v1.3.68)

Date: 2026-11-07 · Tag: `v1.3.68` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.68)

_No release notes._

---

## Version 1.3.67 (v1.3.67)

Date: 2026-11-07 · Tag: `v1.3.67` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.67)

_No release notes._

---

## Version 1.3.66 (v1.3.66)

Date: 2026-11-07 · Tag: `v1.3.66` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.66)

• Fix Voice quick-menu opening Abro (tab index clamp)
• Voice confirm “Recognize voice?” then record + recognize + screenshot
• Launcher shortcuts (Voice/Notes/Clipboard/Rec/Abro) + ASSIST/VOICE_COMMAND entry
• Settings: open assistant settings for home long-press binding

---

## Version 1.3.65 (v1.3.65)

Date: 2026-11-07 · Tag: `v1.3.65` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.65)

• Voice tab (pinned first): assist-gesture capture into a pinned Voice note
• Option to set aNote as the default digital assistant (speech + screenshot)
• Fix Android System page build errors for AccessibilityManager bindings

---

## Version 1.3.63 (v1.3.63)

Date: 2026-11-07 · Tag: `v1.3.63` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.63)

• Add System page (Battery, Hardware, Storage, Processes, Memory, Network) from the overflow menu

---

## Version 1.3.62 (v1.3.62)

Date: 2026-11-06 · Tag: `v1.3.62` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.62)

_No release notes._

---

## Version 1.3.61 (v1.3.61)

Date: 2026-11-06 · Tag: `v1.3.61` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.61)

• Fix crash opening pages: duplicate RedrawCurrentPageMessage messenger subscription

---

## Version 1.3.60 (v1.3.60)

Date: 2026-11-06 · Tag: `v1.3.60` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.60)

• Register Abro as its own browser for link open-with / URLCheck
• Open-in-browser buttons next to links in mail, SMS, clipboard, notes, editor, Telegram
• VirusTotal URL scan in URLCheck (API key + optional auto-scan in Settings)
• Tables in Notes editor (insert / rows / columns)
• Settings: reorder main view tabs
• Move AI providers to aNote Settings; Ask AI on notes, clips, RSS, SMS, files, camera, mail, Telegram
• Live ML Kit barcode/QR detection overlay in main camera

---

## Version 1.3.59 (v1.3.59)

Date: 2026-11-06 · Tag: `v1.3.59` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.59)

• Fix ZXing QR fallback build: use BarcodeFormat.QrCode enum value

---

## Version 1.3.57 (v1.3.57)

Date: 2026-11-06 · Tag: `v1.3.57` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.57)

• Fix QR scan crash: initialize ML Kit before opening the camera scanner

---

## Version 1.3.56 (v1.3.56)

Date: 2026-11-05 · Tag: `v1.3.56` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.56)

• Fix ML Kit QR build: disambiguate BarcodeResult type vs ZXing

---

## Version 1.3.54 (v1.3.54)

Date: 2026-11-05 · Tag: `v1.3.54` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.54)

• Contacts call button places the call through aNote's dialer instead of opening a tel: link

---

## Version 1.3.53 (v1.3.53)

Date: 2026-11-05 · Tag: `v1.3.53` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.53)

• Rename solution/project to anote.sln / anote.csproj
• Update store metadata title and descriptions to aNote
• Keep repository crypto and sync file formats compatible with existing data

---

## Version 1.3.52 (v1.3.52)

Date: 2026-11-05 · Tag: `v1.3.52` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.52)

_No release notes._

---

## Version 1.3.51 (v1.3.51)

Date: 2026-11-04 · Tag: `v1.3.51` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.51)

_No release notes._

---

## Version 1.3.50 (v1.3.50)

Date: 2026-11-03 · Tag: `v1.3.50` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.50)

• Reader editor status line: characters, lines, blanks, words, encoding

---

## Version 1.3.49 (v1.3.49)

Date: 2026-11-03 · Tag: `v1.3.49` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.49)

• Fix Reader editor caret stuck on first line after clicks
• Add arrow/space/tab/enter/end keys on a third toolbar row

---

## Version 1.3.48 (v1.3.48)

Date: 2026-11-03 · Tag: `v1.3.48` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.48)

• Fix Reader crash: MudIconButton OnClick InvalidCastException

---

## Version 1.3.47 (v1.3.47)

Date: 2026-11-03 · Tag: `v1.3.47` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.47)

• Fix MudBlazor Dense analyzer warnings on Reader editor fields

---

## Version 1.3.46 (v1.3.46)

Date: 2026-11-03 · Tag: `v1.3.46` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.46)

• Silence CI warnings: Node 24 Actions, await shopping mode, Telegram next_type flags

---

## Version 1.3.45 (v1.3.45)

Date: 2026-11-03 · Tag: `v1.3.45` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.45)

• Reader editor: larger toolbar, clearer spaces/EOL, selection and active line
• Undo/redo (50 steps), save, clear, find/replace with regex, go to line, duplicate line
• Toolbar icons flash green for 10 seconds after use

---

## Version 1.3.44 (v1.3.44)

Date: 2026-11-03 · Tag: `v1.3.44` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.44)

• Reader text editor: line numbers, EOL marks, space dots, syntax colors
• Editor menu: word wrap, custom font from storage, CSV/HTML/Markdown preview
• Bottom toolbar: copy/paste/cut, select all, jump, copy/cut row

---

## Version 1.3.43 (v1.3.43)

Date: 2026-11-03 · Tag: `v1.3.43` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.43)

• Abro sync merges without duplicating bookmarks, history, or AI providers

---

## Version 1.3.42 (v1.3.42)

Date: 2026-11-03 · Tag: `v1.3.42` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.42)

• Sync Abro bookmarks, history, and AI providers over Booky WebDAV
• Add Sync to Abro three-dots menu; menu layout is now two columns

---

## Version 1.3.41 (v1.3.41)

Date: 2026-11-03 · Tag: `v1.3.41` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.41)

• Add Abro Black mode settings (enable, disable, or per-domain)
• Force black background and white text on matching pages

---

## Version 1.3.40 (v1.3.40)

Date: 2026-11-02 · Tag: `v1.3.40` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.40)

• Fix Abro closing then instantly reopening
• Show password suggestions only on pages with a login form
• Harden accessibility heal so the floating button stays available

---

## Version 1.3.39 (v1.3.39)

Date: 2026-11-02 · Tag: `v1.3.39` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.39)

• Quick menu Apps: icon grid with two-line labels instead of a plain list

---

## Version 1.3.38 (v1.3.38)

Date: 2026-11-02 · Tag: `v1.3.38` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.38)

• Quick menu: Apps button lists installed apps to launch from the same dialog
• Apps list: optional floating/freeform window launch (tap or 🪟 toggle)

---

## Version 1.3.37 (v1.3.37)

Date: 2026-11-02 · Tag: `v1.3.37` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.37)

• Abro opens as its own window/task so aNote stays usable alongside it
• Abro: password vault suggestions and fill for the current site
• Abro: add to Booky / open Booky records from the menu
• Quick menu: more reliable tab opens; Abro launches the browser window

---

## Version 1.3.36 (v1.3.36)

Date: 2026-11-02 · Tag: `v1.3.36` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.36)

• Abro history: share, remove rows, clear all
• Screenshots: open in editor after Full screen; settings for editor app; removed Partial
• Rec: Shots sub-page with screenshot history

---

## Version 1.3.35 (v1.3.35)

Date: 2026-11-02 · Tag: `v1.3.35` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.35)

• Abro: Downloads menu — location, 1–5 threads, download history
• Abro: downloads go to /sdcard/abro/down by default with multi-thread HTTP when supported

---

## Version 1.3.34 (v1.3.34)

Date: 2026-11-02 · Tag: `v1.3.34` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.34)

• Abro Settings: Search — engines, default, suggestions
• Abro Settings: Font — system or custom .ttf/.otf applied to pages

---

## Version 1.3.33 (v1.3.33)

Date: 2026-11-02 · Tag: `v1.3.33` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.33)

• Abro: Ad blocking settings — enable, subscribe to DNS/hosts lists, update/remove
• Abro: filter page requests against downloaded domain lists when enabled

---

## Version 1.3.32 (v1.3.32)

Date: 2026-11-02 · Tag: `v1.3.32` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.32)

• Abro AI: Previous chats page with open and delete
• Abro AI: render Markdown in assistant replies (headers, code, lists)

---

## Version 1.3.31 (v1.3.31)

Date: 2026-11-02 · Tag: `v1.3.31` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.31)

• Abro AI: send mode=chat so Command Code meters chats correctly
• Abro: long-press link menu — share, copy, open in new tab

---

## Version 1.3.30 (v1.3.30)

Date: 2026-11-02 · Tag: `v1.3.30` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.30)

• Fix Abro crash on open (JNI ActionMode hook abort)
• AI: use ⋮ → AI → Chat with selection for selected webpage text

---

## Version 1.3.29 (v1.3.29)

Date: 2026-11-02 · Tag: `v1.3.29` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.29)

• Abro: AI chat via OpenAI-compatible providers (⋮ → AI)
• Abro Settings: manage providers, default model, and AI WebDAV sync
• Select webpage text → AI opens chat with the selection
• Conversations are saved and synced with Booky WebDAV credentials

---

## Version 1.3.28 (v1.3.28)

Date: 2026-10-31 · Tag: `v1.3.28` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.28)

• Rec: Audio/Screen split with screen recording (H.264 576×1024), share and WebDAV
• Quick menu: screen record start/stop
• Calc: unit converters (physics, CS, networking, currency)
• Clock: Compass mode and map links that open in Abro
• Contacts: wider action spacing; Telegram opens chat via phone deep link
• Abro: bookmarks and history shortcuts; Booky links open in Abro

---

## Version 1.3.27 (v1.3.27)

Date: 2026-10-30 · Tag: `v1.3.27` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.27)

• Quick menu: add/remove app and folder shortcuts (long-press to remove)
• Player: open YouTube and other http(s) video/audio URLs
• Contacts Telegram button opens the in-app TG tab
• SMS and TG composers are multiline
• File tab: create new .txt files; path sub-tabs with restore on restart
• Player and Reader: path sub-tabs persisted across restarts
• Main tabs show icons (same as quick menu) instead of text labels
• New Abro light browser tab next to TG (tabs, history, cookies/cache in /sdcard/abro)

---

## Version 1.3.26 (v1.3.26)

Date: 2026-10-28 · Tag: `v1.3.26` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.26)

• RSS no longer auto-fetches on tab open; use Refresh to load new items
• Failed RSS feeds are reported by list number (shown in Feeds)
• Telegram chats show quoted replies with author and jump-to-message link

---

## Version 1.3.25 (v1.3.25)

Date: 2026-10-28 · Tag: `v1.3.25` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.25)

• RSS tab caches articles locally (SQLite) and opens instantly
• New feed items are merged by guid/date; cache capped at ~1 GB
• Cached list stays visible while feeds refresh in the background

---

## Version 1.3.24 (v1.3.24)

Date: 2026-10-27 · Tag: `v1.3.24` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.24)

• New RSS tab next to Contacts: subscribe to feeds, read articles, OPML import/export, WebDAV save
• RSS feeds and read state sync with notes
• Telegram and floating-button improvements; accessibility watchdog helpers

---

## Version 1.3.23 (v1.3.23)

Date: 2026-10-27 · Tag: `v1.3.23` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.23)

Contacts SMS opens the in-app SMS composer with the selected number. SMS tab has a New message button with a typed number field. External sms/smsto intents are handled by aNote.

---

## Version 1.3.22 (v1.3.22)

Date: 2026-10-27 · Tag: `v1.3.22` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.22)

Calc history is now saved. Contacts tab rename. Clock opens on Clock. Camera loads Pictures/aNote, default JPG 25%. SMS newest-first with WebDAV/share. QR generate + WebDAV JPG. Mail account export/import. Player/Reader WebDAV/share. Telegram tab.

---

## Version 1.3.20 (v1.3.20)

Date: 2026-10-21 · Tag: `v1.3.20` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.20)

Add floating-button screenshots (full and partial) with JPG/PNG/WEBP quality settings, and fix screenshot capture by declaring the accessibility screenshot capability.

---

## Version 1.3.19 (v1.3.19)

Date: 2026-10-20 · Tag: `v1.3.19` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.19)

Save Notes, Clip, Sms, QR, Rec, Camera, and Mail items to the WebDAV anote folder with one tap.

---

## Version 1.3.16 (v1.3.16)

Date: 2026-10-20 · Tag: `v1.3.16` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.16)

* Diagnostic logging for Blazor/WebView errors (logcat tags aNoteDiag, aNoteWebView, aNote).

---

## Version 1.3.15 (v1.3.15)

Date: 2026-10-20 · Tag: `v1.3.15` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.15)

aNote version 1.3.15

* Password: new vault tab (after Reader) with master password and optional PIN unlock.
* Vault stored in a separate file encrypted with the Security settings algorithm (XChaCha).
* Add, edit, search, and copy logins; TOTP codes with live countdown.
* Import logins from unencrypted Bitwarden JSON or CSV exports.
* Register as Android Autofill provider for system-wide username/password fill.

---

## Version 1.3.14 (v1.3.14)

Date: 2026-10-19 · Tag: `v1.3.14` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.14)

aNote version 1.3.14

* Player: browse device folders and play audio/video; images open in a viewer.
* Reader: browse and read text, Office (docx/xlsx/pptx/odt/ods), PDF, EPUB and
  FB2 in-app; DjVu and legacy Office open with an external viewer.
* Open media and documents from the system "Open with" / share sheet.

---

## Version 1.3.12 (v1.3.12)

Date: 2026-10-19 · Tag: `v1.3.12` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.12)

aNote version 1.3.12

* Mail: sign in to Gmail with the existing Google account, and add other Google
  mailboxes separately. Note sync still uses only its Drive folder.
* Mail: Gmail app passwords have their displayed spaces removed, and login falls
  back to AUTHENTICATE PLAIN when the server rejects LOGIN.

---

## Version 1.3.11 (v1.3.11)

Date: 2026-10-19 · Tag: `v1.3.11` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.11)

aNote version 1.3.11

* Notes & Checklists: added ability to change text color or background highlight
  color of individual lines or selected text.
* Mail: added a Mail tab in the third row with multi-account IMAP/SMTP support,
  provider presets (Gmail, Outlook, Mail.ru, Yandex, Yahoo, iCloud), folders
  (Inbox, Starred, Sent, Drafts, Trash, All Mail), search, and compose/reply/forward.
* Floating button quick menu: converted from full-screen overlay to a draggable,
  resizable floating window (half screen height by default) with a new Mail tile.
* Camera: fixed horizontal squishing on captured photos by using native Camera2
  still JPEG capture with EXIF orientation and aspect-ratio-preserving preview.

---

## Version 1.3.10 (v1.3.10)

Date: 2026-10-18 · Tag: `v1.3.10` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.10)

aNote version 1.3.10

* Camera: added a Camera tab in the third row with live viewfinder, lens/flash/zoom
  controls, and selectable photo format (JPG, PNG, WEBP) and quality (1-100%).
* Browser / URLCheck: added ability to set aNote as the default system browser
  to inspect, clean tracking parameters, unshorten, and open clicked links in
  any installed browser.
* Floating button: tapping opens a quick menu of all aNote tabs plus a system
  Keyboard Switcher button; configurable swipe up/down/left/right, long press,
  and double click gestures can launch any installed app.

---

## Version 1.3.9 (v1.3.9)

Date: 2026-10-18 · Tag: `v1.3.9` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.9)

aNote version 1.3.9

* Phone: added a number Blocklist with wildcard/mask support (e.g. +7903*,
  8903*, *1234) and automatic rejection of matching incoming calls.
* Phone: added an updatable Spam DB (compatible with AGContacts / Чей номер+
  callerid.mbagc.ru LZMA protobuf shards and Russian spam sources) plus live
  Russian AON and MNP lookup (zvonili.me, whocalls.ru, voprosoff.net,
  zvonili.com, callfilter.app, voxlink, htmlweb, Яндекс.Поиск, 2ГИС, НИИР).

---

## Version 1.3.8 (v1.3.8)

Date: 2026-10-18 · Tag: `v1.3.8` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.8)

aNote version 1.3.8

* Added a third row of tabs: Rec, File and Phone.
* Rec: record sound with pause/resume, play it back, rename, share or save
  recordings to Music/Recordings.
* File: exchange files with a WebDAV server (browse, upload, download, create
  folders, delete). Credentials can be set in the tab or reused from Booky sync.
* Phone: numpad and call history, place calls, and optionally set aNote as the
  default Phone app to handle incoming and outgoing calls.

---

## Version 1.3.7 (v1.3.7)

Date: 2026-10-14 · Tag: `v1.3.7` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.7)

aNote version 1.3.7

* Added a new QR tab. Scan QR codes with the camera, decode them from an image
  file, keep a history of scans, and generate your own QR codes from text or a
  link.

---

## Version 1.3.6 (v1.3.6)

Date: 2026-10-13 · Tag: `v1.3.6` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.6)

aNote version 1.3.6

* Fixed the clipboard history which could stop working completely. When the app
  was interrupted while writing the history file, a leftover marker file blocked
  every following write operation, so neither copied text nor manually added
  items were stored any more. An unfinished writing operation is now completed
  automatically instead of blocking the file.

---

## Version 1.3.5 (v1.3.5)

Date: 2026-10-12 · Tag: `v1.3.5` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.5)

aNote version 1.3.5

* Fixed adding a picture to a note. Three problems were solved: the web view
  was not allowed to downscale the picture, the note page was reloaded when
  returning from the picker (which discarded the picture), and large note
  contents could not be transferred from the editor, so the picture was never
  stored.
* Failing of inserting a picture now shows an error instead of doing nothing.
* Remaining user visible "SilentNotes" texts were renamed to "aNote", e.g. the
  file name of the Android release.

---

## Version 1.3.4 (v1.3.4)

Date: 2026-10-12 · Tag: `v1.3.4` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.4)

aNote version 1.3.4

* Inserting a picture into a note works again, pictures chosen from Google
  Photos and similar providers could not be read before.
* Clipboard: tapping a copied picture opens it in a viewer app, and a new button
  saves it as a JPEG picture to the folder Pictures/clips, named after the
  current date and time.
* Bookmarks: the Booky sync used the encrypted password after the settings were
  saved, which made the synchronization fail. Please enter the WebDAV password
  once more in the settings.
* The Booky sync now shows the reason when it fails.

---

## Version 1.3.3 (v1.3.3)

Date: 2026-10-12 · Tag: `v1.3.3` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.3)

aNote version 1.3.3

* New notes are black by default, matching the rest of the app.
* Black is now available in the note color palette.

---

## Version 1.3.2 (v1.3.2)

Date: 2026-10-12 · Tag: `v1.3.2` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.2)

aNote version 1.3.2

* Black is now the default background of a new installation (instead of the
  cork wallpaper).
* Messages: delete a whole conversation, or select and delete single or multiple
  messages.
* Messages: opening a conversation marks it as read, and all conversations can
  be marked as read at once with "Mark all as read".
* Messages: the default-SMS-app state is now detected correctly, the hint
  disappears once aNote is the default app, and sent messages are shown in the
  conversation again.

---

## Version 1.3.1 (v1.3.1)

Date: 2026-10-12 · Tag: `v1.3.1` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3.1)

aNote version 1.3.1

* A ringing alarm now opens the Clock tab directly, also when the app was not
  running.
* The message input is cleared after a message was sent.

---

## Version 1.3 (v1.3)

Date: 2026-10-12 · Tag: `v1.3` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.3)

aNote version 1.3

* New "Clock" tab: alarms, a world clock with several cities, a timer and a
  stopwatch. Alarms ring with sound and a full-screen notification even when
  aNote is closed, and they survive a reboot.
* New "Sms" tab: view your conversations, read and send text messages. Set
  aNote as the default SMS app to receive incoming messages with notifications.
* The tab bar now uses two rows of four buttons (Notes, Clip, Booky, Phone /
  Calc, Cald, Clock, Sms).
* Plain black background for the clock and the messages pages.

---

## aNote v8.8.7 (v8.8.7)

Date: 2026-10-10 · Tag: `v8.8.7` · [GitHub](https://github.com/e34e4/anote/releases/tag/v8.8.7)

First release of **aNote**, a fork of SilentNotes 8.8.7.

### Highlights
- **Floating note button (Android):** an optional, draggable, translucent pencil button drawn over other apps by an accessibility service; tap it to open aNote. Enable it in *Settings -> User interface*, then turn on the aNote accessibility service in Android settings.
- Rebranded to **aNote** (application id `app.anote`).
- Misc fork updates on top of upstream 8.8.7.

### Install
Download the APK below and sideload it. It is signed with the Android **debug** key, so it installs on a device but cannot upgrade a store-signed installation.

---

## Version 1.2 (v1.2)

Date: 2026-10-10 · Tag: `v1.2` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.2)

aNote version 1.2

* Clipboard history no longer disturbs the keyboard: the flickering overlay which
  periodically stole the window focus was removed. The clipboard is now captured
  when aNote comes to the foreground.
* The clipboard tab has a new text field, to add typed or pasted text to the history.
* Long-press the floating button to save the text selected in any app, including
  web pages, to the clipboard history.
* Selecting or sharing text with "aNote" now stores it in the clipboard history.
  The extra "aNote bookmark" entry was removed.
* Booky: bookmarks can be synchronized with the shared Booky bookmarks file over
  WebDAV, so the browser extension and aNote keep the same bookmarks.
* Plain black cards for the clipboard, phone and bookmark lists.
* The app is English only now.

---

## Version 1.1 (v1.1)

Date: 2026-10-10 · Tag: `v1.1` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.1)

aNote version 1.1

* New "Phone" tab with your contacts: search, add/edit/delete, copy numbers,
  call, SMS and Telegram, plus import from a contacts file.
* Contacts are stored in the repository and synchronized like notes.
* Booky bookmarks tab.
* Clipboard history is now synchronized together with the notes.
* Plain black tab bar, clipboard cards and contact cards.

---

## Version 1.0 (v1.0)

Date: 2026-10-10 · Tag: `v1.0` · [GitHub](https://github.com/e34e4/anote/releases/tag/v1.0)

aNote version 1.0

* Fork of SilentNotes, renamed to aNote.
* Android: floating note button (accessibility service).
* Android: clipboard monitor with a clipboard history tab.
* Plain black splash screen and a configurable app background color.
