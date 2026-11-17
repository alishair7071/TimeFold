# TimeFold — 10 Phases

**How to use this file:** read only the phase you are working on. Do not read the whole file.

Each phase has:
- **Goal** — one sentence.
- **After this phase** — what you can actually do.
- **Decisions** — your options, my recommendation, and my reason. You choose.
- **Done when** — a short checklist.

Nothing here is final. If a choice does not fit you later, change it and write the new choice in
the table below.

---

## What TimeFold is

A Windows app that quietly records what you do on your computer, so that months later you can ask:
*"What was I doing on the 10th at 3 PM?"*

---

## Your decisions (fill this in as you go)

| Phase | Decision | My recommendation | Your choice |
|---|---|---|---|
| 1 | Language / framework | C# + .NET 8 | **DECIDED — C# + .NET 8** |
| 1 | Data storage | SQLite | **DECIDED — SQLite** |
| 1 | Detect active window | Check every 1 second | **DECIDED — every 1 second** |
| 1 | Start with Windows | Registry Run key | **DECIDED — no autostart.** Changed 2026-11-16: TimeFold runs only when you open the desktop shortcut. The code to turn it on still exists, unused, for a Phase 2 switch. |
| 1 | Installer | Inno Setup | **DECIDED — Inno Setup** |
| 1 | Shortest record kept | 2 seconds | **DECIDED — 2 seconds.** Anything shorter is Alt-Tab noise, not work. |
| 1 | Unread counters in titles | Strip them | **DECIDED — strip.** "(490) Inbox" and "(491) Inbox" are the same window; without this Chrome alone made hundreds of junk records a day. |
| 1 | How we notice a change | Poll every 1 second | **DECIDED — keep polling.** A Windows event hook uses less power but can be dropped silently, leaving a gap in your history with no error. A ticking timer can be seen. |
| 1 | First run | Open the window once | **DECIDED — open once.** Windows hides new tray icons, so otherwise nothing appears to happen. |
| 1 | TimeFold recording itself | Block it | **DECIDED — blocked.** Reading your own timeline should not create records about reading your own timeline. |
| 2 | Block sensitive apps by | Program file path | **DECIDED — all three.** Program name is the main one, and the Blocked apps screen lists your installed apps so you tick instead of typing. Folder and window-title rules are on an Advanced tab. |
| 2 | Encrypt database | No, not yet | **DECIDED — no encryption yet.** Your Windows account already keeps other users out, and an encrypted file could not be opened by hand to check that blocking works. Revisit at Phase 5, when screenshots arrive. |
| 2 | How long to keep data | Forever (text is tiny) | **DECIDED — keep forever.** Around 1–5 MB a month, so ten years fits in under 600 MB. Deleting is manual and yours to choose. Screenshots get a hard limit in Phase 5. |
| 2 | Being away from the computer | Record it, do not hide it | **DECIDED — record as its own kind of entry.** 5 minutes without keyboard or mouse counts as away. A locked screen is recorded as locked. Neither records what was on screen. |
| 3 | Search engine | SQLite FTS5 | |
| 3 | Timeline look | Grouped list | |
| 4 | Browser tracking method | Read browser history file | |
| 4 | Which browsers first | Chrome + Edge | |
| 5 | Screenshot method | CopyFromScreen (simple) | |
| 5 | Image format | WebP | |
| 5 | Which monitor | The one you are using | |
| 5 | Disk limit | 10 GB, delete oldest | |
| 6 | OCR engine | Windows built-in OCR | |
| 6 | OCR when | Everything, but slowly | |
| 7 | Grouping method | Time gaps + rules | |
| 8 | AI model | Cloud API, opt-in | |
| 8 | How AI finds things | Search only, no embeddings | |
| 9 | Cloud storage | Encrypted file backup | |
| 10 | Code signing | Azure Trusted Signing | |
| 10 | Auto-update | Velopack | |

**Note:** my recommendations from Phase 5 onward assume you pick C# in Phase 1. If you pick Rust
instead, I note the Rust equivalent inside each phase.

---

# Phase 1 — Make it installable and working

**Goal:** a real installer that installs a real app that really records what you do.

**After this phase:** you install TimeFold, it starts with Windows, it sits in the tray, and it
shows you a list of every app you used today with the exact times.

This is the most important phase. It is small on purpose. Everything later is added on top of it.

---

### Decision 1.1 — Which language and framework?

| Option | Good | Bad |
|---|---|---|
| **C# + .NET 8 + WPF** | Windows APIs are one line of code. One language for everything. Tray icon, autostart, installer are all solved problems. | Install size is 60–80 MB if you bundle .NET. Uses a bit more RAM. |
| **Rust + Tauri 2 + React** | Smallest app. Lowest RAM. Very fast. | Rust is hard to learn. You write two languages (Rust + TypeScript). Needs WebView2 installed. |
| **Electron + Node.js** | You already know web code. Huge ecosystem. | Heavy (~150 MB RAM) for an app that never closes. Still needs native code for Windows APIs. |
| **Python + PySide** | Fastest to write a prototype. | Making a clean installer is painful. Bad choice for an app that runs all day. |

**My recommendation: C# + .NET 8.**

**Why:** Phase 1 is mostly Windows API work — read the active window, read the process name, detect
idle, make a tray icon, start with Windows, build an installer. C# does all of this with built-in
libraries and no glue code. Rust would give you a lighter app, but you would spend most of Phase 1
fighting the language instead of building the product. You can still switch to Rust later: the
database file stays the same, so you lose code but you lose **zero data**.

**Choose Rust instead if:** you actually want to learn Rust on this project, and you are okay with
Phase 1 taking 3–4x longer.

`Your choice: __________As your recommendation__________`

---

### Decision 1.2 — Where do we store the data?

| Option | Good | Bad |
|---|---|---|
| **SQLite** | One file. No server. Very fast. Built-in full-text search for Phase 3. You can open it with any SQLite tool to check your data. | You write some SQL. |
| **JSON files per day** | Dead simple to start. | Searching months of data becomes very slow. You would have to replace it by Phase 3. |
| **LiteDB (C# only)** | No SQL, saves C# objects directly. | Weaker search. Smaller community. Locks you into C#. |

**My recommendation: SQLite.**

**Why:** it is the only option that survives all 10 phases without being replaced. It handles
millions of rows, it gives you free full-text search in Phase 3, and the file does not care which
language wrote it — so it protects you if you change your mind about Decision 1.1.

`Your choice: ` **SQLite** — first tried JSON, changed on 2026-11-16 because JSON would have had to
be replaced in Phase 3 anyway, and in C# saving to SQLite is no more code than writing JSON.

---

### Decision 1.3 — How do we detect which app you are using?

| Option | Good | Bad |
|---|---|---|
| **Check every 1 second** (`GetForegroundWindow` in a timer) | Very simple. Always works. ~20 lines of code. | You can miss a window you used for under a second. |
| **Windows event hook** (`SetWinEventHook`) | Instant. No wasted checks. | More complex. Hooks sometimes stop working silently and you must detect that. |

**My recommendation: check every 1 second.**

**Why:** the cost is almost nothing (one API call per second is far less work than moving your
mouse), and it cannot silently break. Missing a window you looked at for under a second does not
matter for a memory app — you will not be searching for that. Add the event hook later, in Phase 3
or beyond, only if you find the 1-second gap actually loses something you wanted.

`Your choice: _____every second_______________`

---

### Decision 1.4 — How does the app start with Windows?

| Option | Good | Bad |
|---|---|---|
| **Registry Run key** (`HKCU\...\Run`) | Simple. No admin rights needed. Easy to turn off. | Starts slightly after login. |
| **Task Scheduler** | More control, can retry on failure. | More complex, sometimes needs admin. |
| **Startup folder shortcut** | Easiest of all. | Users delete it by accident. Hard to manage from code. |
| **Windows Service** | Starts before login. | **Cannot be used.** A service cannot see your desktop, so it cannot read the active window or take screenshots. |

**My recommendation: Registry Run key.**

**Why:** it needs no admin rights, it is three lines of code, and you can offer an on/off switch in
settings. Note that Windows Service is not a real option here — Windows isolates services from your
desktop session, which would break the whole app from Phase 1 onward.

`Your choice: _______as you recommended_____________`

---

### Decision 1.5 — How do we build the installer?

| Option | Good | Bad |
|---|---|---|
| **Inno Setup** | Free. Simple script. Installs per-user with no admin. Handles uninstall. | Extra tool to learn (but it is small). |
| **WiX / MSI** | The Windows standard. Enterprise-friendly. | Painful XML. Slow to learn. |
| **MSIX** | Modern, clean install/uninstall, Store-ready. | Needs a signing certificate even for testing. Restricted — may block the background recording you need. |
| **Just a .zip** | Zero work. | No autostart setup, no uninstall, no upgrade. Not really "installable". |

**My recommendation: Inno Setup.**

**Why:** it gives you a normal `.exe` installer with per-user install, no admin prompt, and a proper
uninstaller — which is exactly the Phase 1 goal. MSIX sounds better but its sandbox can block
background monitoring, and it forces the signing-certificate problem into Phase 1 instead of
Phase 10 where it belongs.

`Your choice: ________as you recommended____________`

---

### Done when

- [ ] Installer runs on a clean Windows 10 **and** 11 machine without admin rights.
- [ ] App does **not** start by itself. Opening the desktop shortcut starts recording, and
      recording continues with the window closed.
      *Trade-off to watch:* reboot and forget to open it, and that day is simply not recorded.
      Phase 2 should make that gap visible.
- [ ] The list of apps you used today matches what you actually did.
- [ ] Times are correct (stored as UTC inside, shown in your local time).
- [ ] Runs 48 hours without crashing and without filling the disk with logs.
- [ ] Pause really stops recording (check the database, not the screen).
- [ ] Uninstall removes the app and asks whether to delete your data.

### Before moving on, ask yourself

Is the window title actually useful for remembering? Open the app and read yesterday. If it means
nothing to you, tell me — we fix that before building anything on top.

---

# Phase 2 — Make it safe to leave running

**Goal:** you can trust it with months of your real life.

**After this phase:** you can block apps, pause anytime, delete any part of your history, and
export everything.

**Why this comes before screenshots and browser tracking:** after Phase 4 and 5 your history gets
personal fast. The controls must exist *before* the data does.

---

### Decision 2.1 — How do we identify an app to block?

| Option | Good | Bad |
|---|---|---|
| **Program file path** (`C:\...\KeePass.exe`) | Exact. Cannot be fooled by a renamed window. | Breaks if the app moves or updates its folder. |
| **Program name** (`KeePass`) | Survives updates and folder changes. | Two different apps can share a name. |
| **Window title text** (e.g. anything containing "Password") | Catches sensitive content inside an allowed app. | Risky — easy to over-block or under-block. |

**My recommendation: all three, with program file path as the main one.**

**Why:** one method alone always has a hole. Path is precise, name is a safety net when the app
updates, and title matching catches the case where a normal app shows something private. The
important part is the rule: **if we are not sure, record nothing.** It is better to lose one boring
entry than to save one private one.

`Your choice: ` **All three, program name first** — and rather than typing file names, the
Blocked apps screen lists the apps on this computer so you tick one to block it. Folder and
window-title rules live on an Advanced tab for the cases a list of apps cannot express.

---

### Decision 2.2 — Should the database be encrypted?

| Option | Good | Bad |
|---|---|---|
| **No encryption** | Simple. Your Windows account password already blocks other users. | Anyone who steals the laptop and gets past Windows can read the file. |
| **SQLCipher** (encrypted SQLite) | File is unreadable without the key. | You must handle the key. You lose the ability to open the file with normal SQLite tools. Slightly slower. |
| **Windows DPAPI** | Key is tied to your Windows account, no password to remember. | Only protects against other users, not against someone with your account. |

**My recommendation: no encryption yet. Revisit at the end of this phase.**

**Why:** your Windows account already protects the file from other users on the same PC, and in
Phase 2 you still need to open the database by hand constantly to verify that blocking works.
Encrypting it now makes debugging harder for very little real gain.

**Add SQLCipher when:** you enable BitLocker-free laptop use, share the PC, or start Phase 5
(screenshots), whichever comes first. Screenshots are far more sensitive than text.

`Your choice: ______no encryption yet______________`

---

### Decision 2.3 — How long do we keep data?

| Option | Good | Bad |
|---|---|---|
| **Forever** | Nothing is ever lost. This is a memory app, after all. | The database grows — but slowly. |
| **Keep N months** | Predictable size. | You will eventually want something you deleted. |
| **Size limit, delete oldest** | Never runs out of disk. | Deletes by age, not importance. |

**My recommendation: keep text forever. Add a size limit only for screenshots in Phase 5.**

**Why:** the maths is on your side. App and window records are plain text — roughly 1–5 MB per
month, so ten years fits in under 600 MB. There is no reason to throw away the thing the product
exists to keep. Screenshots are the opposite (gigabytes per month), so they get a hard limit when
they arrive.

`Your choice: ______add option to choose the user if user want 6 months or 1 month or one year it should work for each user and aslo there should be two different options for screen shots and for text, but still we are working with just text so still just implement for the text When we implement the screenshot, then we will also implement this option for the screenshots in the next phases. ______________`

---

### Done when

- [ ] A blocked app creates **zero** rows — check by opening the database directly.
- [ ] Adding a block offers to delete that app's past history too.
- [ ] Locked screen and idle time are not recorded as activity.
- [ ] You can delete one entry, one day, one app, or everything.
- [ ] You can export everything and restore it.
- [ ] Seven days of real use with no problems.

### Before moving on, ask yourself

Would you be comfortable if a friend installed this on their PC today? If no — what exactly stops
you? That is the rest of Phase 2.

---

# Phase 3 — Timeline and search

**Goal:** stop being a log. Start being a memory you can actually query.

**After this phase:** you can jump to any date, scroll months of history, and search by keyword.

---

### Decision 3.1 — Which search engine?

| Option | Good | Bad |
|---|---|---|
| **SQLite FTS5** | Already inside SQLite — nothing to install. Fast on millions of rows. Ranks results. | Basic ranking. No typo tolerance. |
| **SQL `LIKE '%word%'`** | Zero setup. | Gets painfully slow after a few months of data. Cannot rank. |
| **Tantivy / Lucene** | Best ranking and features. | A whole second storage system to install, sync and keep consistent. |

**My recommendation: SQLite FTS5.**

**Why:** it is free, it is already in your database, and it is fast enough for far more data than
you will ever generate. Most importantly it stays **inside** SQLite, so when you delete something
it disappears from search in the same transaction. A separate search index can drift out of sync
with your data, and in a privacy app a deleted item that still shows up in search is a serious bug.

`Your choice: ____________________`

---

### Decision 3.2 — What should the timeline look like?

| Option | Good | Bad |
|---|---|---|
| **Grouped list** (like a chat log, newest first, grouped by hour) | Easy to build. Easy to read. Works on any screen size. | Hard to see the shape of a whole day at a glance. |
| **Horizontal bars** (one row per app, time on the X axis) | Beautiful. You see your whole day instantly. | Much more work. Awkward with 40 different apps. |
| **Calendar heatmap** | Great for picking a day out of a month. | Tells you nothing about what you did. Needs a second view anyway. |

**My recommendation: grouped list now; add the calendar heatmap as a date-picker later.**

**Why:** the question this product answers is "what was I doing at 3 PM?" — and a list answers that
directly. Horizontal bars answer "how did I spend my day", which is a productivity question, not a
memory question. Build the thing that answers your actual question first.

`Your choice: ____________________`

---

### Done when

- [ ] Search across 90+ days of real data returns in under about 0.2 seconds.
- [ ] You can jump to any date and time.
- [ ] Scrolling a month of history stays smooth.
- [ ] Deleting data removes it from search immediately.
- [ ] You answer three real "what was I doing when…" questions using only the app.
- [ ] Everything works with Wi-Fi turned off.

---

# Phase 4 — Browser history

**Goal:** record which websites you visited, not just "Chrome was open".

**After this phase:** you can find a site you visited a month ago even if you forgot its name.

This is the biggest jump in usefulness in the whole project. A window title tells you a tab
existed. The URL tells you what it was.

---

### Decision 4.1 — How do we get the URLs?

| Option | Good | Bad |
|---|---|---|
| **Read the browser's own history file** (Chrome/Edge/Firefox keep a SQLite file) | No extension to install. Works for all Chromium browsers at once. You also get history from *before* TimeFold was installed. | Not instant (read every few minutes). Misses in-page navigation. The file is locked while the browser runs, so you must copy it first. |
| **Browser extension + native messaging** | Instant and accurate. Sees every navigation. | You must install an extension in every browser, register a native messaging host, and maintain the extension. Users can disable it silently. |
| **UI Automation on the address bar** | No extension, no file reading. | Fragile. Breaks with every browser UI change. Feels like a hack. |

**My recommendation: read the browser's history file.**

**Why:** it gives you about 90% of the value for about 20% of the work, and it works the moment you
ship it — no extension to install, no browser store review, no per-browser maintenance. It even
backfills history from before you installed TimeFold, which no extension can do. Add an extension
later only if you find the delay actually bothers you.

**Important detail:** copy the history file to a temp location before reading it, because the
browser holds a lock on the original.

`Your choice: ____________________`

---

### Decision 4.2 — Which browsers first?

| Option | Good | Bad |
|---|---|---|
| **Chrome + Edge** | Same engine, same file format — one piece of code covers both. Covers ~85% of Windows users. | Firefox users get nothing yet. |
| **All three at once** | Complete. | Firefox uses a different schema, so it is a second implementation. |
| **Only your own browser** | Fastest. | You will have to redo this phase. |

**My recommendation: Chrome + Edge, then Firefox.**

**Why:** Chrome and Edge store history in the same format, so supporting both is almost free once
you support one. Firefox is a separate job — do it after you have proved the approach works.

`Your choice: ____________________`

---

### Done when

- [ ] Visits appear with correct times, matched to when that browser was in focus.
- [ ] Private / incognito windows produce nothing.
- [ ] Blocked websites produce nothing.
- [ ] Turning browser tracking off leaves the rest of the app working.
- [ ] You find a site from 30+ days ago that you could not have found before.

---

# Phase 5 — Screenshots

**Goal:** add visual memory — sometimes you remember what the screen looked like, not what it was
called.

**After this phase:** you can look at what your screen actually showed at a given moment.

**This is the riskiest phase.** It is the most private data, the biggest disk cost, and the most
likely component to crash. Treat it carefully.

---

### Decision 5.1 — How do we capture the screen?

| Option | Good | Bad |
|---|---|---|
| **`CopyFromScreen` / BitBlt** | About 5 lines of code. Works everywhere. | Captures the whole monitor, not one window. Can miss hardware-accelerated content. Can catch an overlapping window. |
| **`Windows.Graphics.Capture`** | The modern, correct API. Can capture one specific window. Handles hardware-accelerated content. | More complex setup. Windows 10 1803+ only. |
| **DXGI Desktop Duplication** | Fastest, built for video. | Overkill — designed for 60 fps streaming, not one shot per minute. |

**My recommendation: `CopyFromScreen` first, then move to `Windows.Graphics.Capture`.**

**Why:** Phase 5 has four other hard problems to solve (deduplication, compression, disk limits,
crash isolation). Start with the capture method that takes an afternoon so you can spend your time
on those. Switch to `Windows.Graphics.Capture` when you want per-window capture — the rest of your
code will not change, because capture sits behind one small interface.

*(In Rust: `screenshots` crate first, `windows-capture` later.)*

`Your choice: ____________________`

---

### Decision 5.2 — Which image format?

| Option | Size per screenshot | Notes |
|---|---|---|
| **PNG** | ~1–3 MB | Perfect quality, far too big. 30 GB/month. |
| **JPEG (quality 75)** | ~150–300 KB | Universal support. Text looks slightly fuzzy. |
| **WebP (quality 70)** | ~80–150 KB | ~30% smaller than JPEG at the same quality. Supported everywhere now. |
| **AVIF** | ~50–100 KB | Smallest, but slow to encode and CPU-heavy. |

**My recommendation: WebP at quality ~70.**

**Why:** at roughly 100 KB per screenshot, one screenshot per minute of active use comes out around
1–2 GB per month — which fits inside a sensible disk limit. PNG would be 20x that. AVIF saves a
little more but burns CPU on a machine you are trying to keep quiet.

`Your choice: ____________________`

---

### Decision 5.3 — Which monitor, if you have several?

| Option | Good | Bad |
|---|---|---|
| **The monitor with the active window** | Captures what you were looking at. One image. | Misses what was on the other screen. |
| **All monitors, one image each** | Complete. | 2–3x the disk use. Most of it is a window you never looked at. |
| **Primary monitor only** | Simplest. | Wrong whenever you work on the second screen. |

**My recommendation: the monitor with the active window.**

**Why:** it matches what the product is for. You are trying to remember what you were doing, and
you were doing it on the screen you were looking at. Capturing all monitors triples your disk cost
to store mostly idle windows.

`Your choice: ____________________`

---

### Decision 5.4 — Disk limit

**My recommendation: a hard 10 GB cap, delete oldest screenshots first, and show current usage in
settings.**

**Why:** screenshots are the only part of TimeFold that can realistically fill a disk, so the limit
must be enforced by code, not by good intentions. 10 GB at ~100 KB per image is roughly 100,000
screenshots — about 6–12 months of normal use. Text records are never deleted by this rule, so even
after screenshots expire you still know *what* you were doing, just not what it looked like.

Also required in this phase:
- **Check the block rules before taking the picture**, not after. A screenshot of your bank must
  never exist in memory, let alone on disk.
- **Skip near-identical screenshots** using a perceptual hash. Most of your screen does not change
  minute to minute, and this alone cuts storage by over half.
- **Isolate from the recorder.** If screenshots crash, app tracking must keep working. Start with a
  separate thread; move to a separate process if you see crashes.

`Your choice: ____________________`

---

### Done when

- [ ] Blocked apps produce no screenshot and no screenshot record.
- [ ] No screenshots while idle or on the lock screen.
- [ ] Near-duplicate screenshots are skipped.
- [ ] Disk use stays under the limit automatically over a week.
- [ ] Killing the screenshot component does not stop app tracking.
- [ ] No database row points to a missing file, and no file is left without a row.

---

# Phase 6 — Read the text inside screenshots (OCR)

**Goal:** make screenshots searchable, not just viewable.

**After this phase:** you can search for text that was only ever *on your screen* — an error
message, a name in a PDF, a line in a video.

---

### Decision 6.1 — Which OCR engine?

| Option | Good | Bad |
|---|---|---|
| **Windows built-in OCR** (`Windows.Media.Ocr`) | Already on every Windows 10/11 machine. Free. Fast. Nothing to install or ship. | Quality is good, not excellent. Fixed set of languages. |
| **Tesseract** | Best-known open source OCR. Many languages. | You must ship ~30 MB of binaries and language data. Slower. Struggles with screen UI text. |
| **PaddleOCR / a local model** | Best accuracy on hard images. | Hundreds of MB. Needs Python or ONNX runtime. Heavy CPU/GPU. |

**My recommendation: Windows built-in OCR.**

**Why:** your input is screen text — clean, sharp, already rendered by a computer. That is the easy
case for OCR, and the built-in engine handles it well. The others are built for photographed or
scanned documents, which is a much harder problem you do not have. It also adds zero megabytes to
your installer, which matters for a background app.

*(In Rust: call the same Windows API through `windows-rs`.)*

`Your choice: ____________________`

---

### Decision 6.2 — Run OCR on everything, or only when asked?

| Option | Good | Bad |
|---|---|---|
| **Everything, in the background** | Search just works. No waiting. | Constant CPU work. Will be noticed on a laptop. |
| **Only when you search** | Zero background cost. | First search takes minutes. Unusable. |
| **Everything, but throttled** | Search works, and you never notice the CPU. | Recent screenshots are searchable a bit late. |

**My recommendation: everything, but throttled.**

**Why:** OCR is the first thing in TimeFold that uses real CPU, and the one unbreakable rule of this
app is that you should never feel it running. So: process the queue slowly, pause when you are on
battery or the CPU is busy, always yield to the recorder, and keep your place in the queue across
restarts. A screenshot becoming searchable 20 minutes late costs you nothing. A laptop fan spinning
up costs you the user.

Also: if OCR fails on an image, **keep the image**. Mark it failed and move on.

`Your choice: ____________________`

---

### Done when

- [ ] You search for text you only saw in a screenshot, and you find it.
- [ ] OCR never makes the computer feel slow.
- [ ] Closing the app mid-queue and reopening it resumes where it stopped.
- [ ] A failed OCR never deletes or corrupts the screenshot.

---

# Phase 7 — Group activity into sessions

**Goal:** turn thousands of separate entries into a handful of meaningful work sessions.

**After this phase:** instead of 200 rows, you see *"Working on the login bug — 2:15 PM to 3:40 PM —
VS Code, Chrome (GitHub, Stack Overflow), Terminal."*

---

### Decision 7.1 — How do we group?

| Option | Good | Bad |
|---|---|---|
| **Time gaps + rules** (a gap over N minutes ends a session; apps used together belong together) | Understandable. Debuggable. You can explain every grouping. | Not clever. Will mis-group sometimes. |
| **Clustering / ML** | Can find patterns you did not think of. | A black box. When it is wrong you cannot tell why. Needs lots of your data to train. |
| **Ask you to label manually** | Perfectly accurate. | Nobody labels their own history. It will not get used. |

**My recommendation: time gaps + rules.**

**Why:** grouping is a guess, and a guess you can explain is far more trustworthy than a better
guess you cannot. Start with: a gap of more than 10 minutes ends a session; apps and sites used
within the same window of time belong together; names from file paths and window titles suggest a
title. You can improve the rules forever, and you can always see why a session looks the way it
does.

**One required rule:** store sessions in their **own table**, built *from* the raw records and
rebuildable at any time. Never edit the original records to add grouping. That way a better
algorithm later can throw away every session and rebuild, without touching your actual history.

**One more:** show clearly whether a session title was guessed by TimeFold or confirmed by you.
These must never look the same.

`Your choice: ____________________`

---

### Done when

- [ ] A day you remember well is grouped into sessions you recognise.
- [ ] You can split, merge, rename and delete a session.
- [ ] Deleting all sessions and rebuilding them loses nothing.
- [ ] Guessed titles are visually distinct from titles you confirmed.

---

# Phase 8 — Ask your memory

**Goal:** ask questions in plain language instead of building a search query.

**After this phase:** you type *"what was I working on last Tuesday afternoon?"* and get an answer
with links to the actual records that prove it.

---

### Decision 8.1 — Which AI model?

| Option | Good | Bad |
|---|---|---|
| **Cloud API (Claude / GPT), opt-in** | Much better answers. Nothing to install. No GPU needed. | Some of your activity text leaves your machine. Costs money per question. Needs internet. |
| **Local model (Ollama, Llama / Qwen)** | Nothing ever leaves your computer. Free. Works offline. | Needs 4–8 GB of RAM while running. Noticeably weaker at reasoning over evidence. A big download. |
| **Both, user picks** | Privacy-conscious users pick local; others get quality. | Two code paths to maintain and test. |

**My recommendation: build a provider interface, ship the cloud API first, turned off by default.**

**Why:** this phase only works if the answers are good enough to trust, and today a cloud model is
clearly better at reading 30 records and answering carefully without inventing anything. But this
is a privacy product, so it must be **opt-in, off by default, and honest**: tell the user exactly
what gets sent (a few text records, never screenshots, never the whole database). Because you write
it behind an interface, adding Ollama later is a small job — and local models keep improving.

**Never:** send screenshots or the whole database. Only the small set of text records needed to
answer the question that was asked.

`Your choice: ____________________`

---

### Decision 8.2 — How does the AI find the relevant records?

| Option | Good | Bad |
|---|---|---|
| **Search + filters only** (reuse Phase 3) | No new system. Fast. Already built and tested. | Misses questions where you use different words than the record does. |
| **Add embeddings / vector search** | Finds things by meaning, not exact words. | A whole new index to build, store and keep in sync. Needs an embedding model running locally. |

**My recommendation: search + filters only. Add embeddings only if that measurably fails.**

**Why:** most real questions are about **time** ("last Tuesday afternoon", "around 3 PM on the
10th"), and time filters answer those perfectly with zero new machinery. For word questions, the
AI can expand your question into several searches — asking "kubernetes" *and* "k8s" *and* "docker"
costs nothing. Write down the questions that genuinely fail, and only then decide whether
embeddings are worth their cost.

`Your choice: ____________________`

---

### The rule that matters most in this phase

**The AI may only see what the search returned, and every answer must link to real records.**

If the search found nothing, the answer is *"your history doesn't show anything then"* — never a
guess. An AI that invents a plausible activity destroys the whole point of a memory product,
because you would no longer be able to trust anything it tells you.

Also: Phase 3 search must keep working perfectly with no AI configured at all.

### Done when

- [ ] Every answer links to records you can click and verify.
- [ ] Asking about a day you were on holiday gets "nothing recorded", not a story.
- [ ] Turning AI off leaves the app fully usable.
- [ ] The app clearly shows what would be sent out before you turn the cloud option on.

---

# Phase 9 — Backup and multiple devices

**Goal:** your memory survives a dead laptop.

**After this phase:** your history is backed up (encrypted), and you can restore it on a new PC.

---

### Decision 9.1 — Where does the backup go?

| Option | Good | Bad |
|---|---|---|
| **Encrypted file into a folder you choose** (OneDrive, Google Drive, a USB disk) | No server to build or pay for. No accounts. You own the storage. Works today. | No real-time sync between two PCs at once. Conflicts if two PCs write together. |
| **S3-compatible storage** (Cloudflare R2, Backblaze B2) | Cheap. Scales. Supports proper incremental sync. | The user must create their own keys, or you must run a service to hand them out. |
| **Your own backend + accounts** | Best experience. Real multi-device sync. | You now run a service, store other people's data, and own that responsibility. Big job. |

**My recommendation: encrypted file backup into a folder the user chooses.**

**Why:** it delivers the thing that actually matters — *you do not lose your memory if the laptop
dies* — for a fraction of the work, and with no liability for you. Real-time two-way sync between
two PCs is a much harder problem (conflicts, duplicates, ordering) and most people only have one
main computer. Solve backup first. Build sync only if you genuinely need two machines.

`Your choice: ____________________`

---

### Decision 9.2 — How is the backup encrypted?

**My recommendation: encrypt on your computer, before upload, with a key derived from a passphrase
you choose — plus a printable recovery code.**

**Why:** the backup is going to sit in someone else's cloud folder. If it is readable there, every
privacy promise TimeFold made in Phases 2 and 5 is cancelled. Encrypting before it leaves the
machine means the storage provider holds nothing but noise.

The hard part is honest: **if the passphrase and the recovery code are both lost, the backup is
gone.** There is no reset. The app must say this plainly, more than once, when the feature is
switched on.

`Your choice: ____________________`

---

### Done when

- [ ] A backup can be restored onto a clean PC and the history is complete.
- [ ] The backup file is unreadable without the passphrase (verify it by hand).
- [ ] Turning backup off, or losing internet, does not affect recording or search.
- [ ] The warning about key loss is impossible to miss.

---

# Phase 10 — Release it properly

**Goal:** turn a working app into something you can hand to another person.

**After this phase:** anyone can download, install, trust and update TimeFold.

---

### Decision 10.1 — Code signing

| Option | Cost | Result |
|---|---|---|
| **No signing** | Free | Windows SmartScreen shows a scary warning. Most people cancel the install. |
| **Azure Trusted Signing** | ~$10/month | Trusted immediately. No hardware token. Microsoft-run. |
| **OV certificate** (Sectigo, etc.) | ~$200–400/year | Works, but reputation builds slowly — warnings persist at first. |
| **EV certificate** | ~$400–700/year | Instant trust, but needs a hardware token and a company identity. |

**My recommendation: Azure Trusted Signing.**

**Why:** it is by far the cheapest way to make the SmartScreen warning disappear, and it needs no
hardware token. For a background app that asks for a lot of trust, an unsigned installer is a
serious problem — people are right to be suspicious of software that records their screen.

**Note:** it requires a verified identity (individual verification is available). Start that process
early, as it takes days.

`Your choice: ____________________`

---

### Decision 10.2 — Auto-update

| Option | Good | Bad |
|---|---|---|
| **Velopack** | Modern, simple, made for desktop apps. Delta updates. Works with .NET and Rust. | A newer project. |
| **Squirrel.Windows** | Long-established. | Barely maintained now. |
| **Microsoft Store** | Updates handled for you. Trust is built in. | Store sandbox may block background monitoring. Review process. Less control. |
| **Manual download** | No work. | Nobody updates. Your bug fixes never reach anyone. |

**My recommendation: Velopack.**

**Why:** it is the lowest-friction way to get real auto-updates on Windows, it supports delta
updates (small downloads), and it works with either Phase 1 language choice. The Store is tempting
for trust, but its sandbox restrictions are a genuine risk for an app whose whole job is watching
other apps.

`Your choice: ____________________`

---

### Also in this phase

- **First-run setup:** explain clearly what is recorded, let the user set blocks and screenshot
  settings before any recording begins. Earn trust in the first 30 seconds.
- **A written privacy statement** in plain words: what is collected, where it is stored, what
  leaves the machine (and when nothing does).
- **Crash reporting** that is opt-in and contains no activity data.
- **Distribution:** GitHub Releases is enough to start, and free.
- **Performance check** on a slow laptop, not just yours.

### Done when

- [ ] A stranger can install it without seeing a security warning.
- [ ] An update installs itself and loses no data.
- [ ] A new user understands what is being recorded before it starts.
- [ ] It feels invisible on a low-end laptop.
