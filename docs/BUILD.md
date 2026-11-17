# How to build and run TimeFold

## What you need, once

```powershell
winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
winget install JRSoftware.InnoSetup --accept-source-agreements --accept-package-agreements
```

Open a **new** terminal afterwards, so `dotnet` is found. Check it worked:

```powershell
dotnet --version
```

---

## Run it while developing

```powershell
cd d:\TimeFold
dotnet run --project src\TimeFold.App\TimeFold.App.csproj
```

The app starts **invisibly**. Look for its icon near the clock, at the bottom right
of your screen. Double-click the icon to open the window.

To stop it: right-click the tray icon, then Quit. Closing the window does not stop it.

---

## Just check that it compiles

```powershell
dotnet build
```

---

## Make the installer

Two steps. First package the app into one folder:

```powershell
dotnet publish src\TimeFold.App\TimeFold.App.csproj -c Release -r win-x64 --self-contained true -o publish
```

`--self-contained true` means .NET is included inside the app, so it runs on a
computer that has never had .NET installed. It makes the download bigger
(~150 MB) but the install never fails with "please install .NET first".

Then build the installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\TimeFold.iss
```

The result appears in `installer\output\TimeFold-Setup-0.1.0.exe`.

Windows will show a blue "unknown publisher" warning when you run it. That is
expected and is fixed in Phase 10 with code signing.

---

## Where your data lives

```
C:\Users\<you>\AppData\Local\TimeFold\
├── timefold.db     everything TimeFold has recorded
└── logs\           what the app did (never what you did)
```

To start completely fresh, quit TimeFold and delete that whole folder.

---

## How to check it is really working

Do not trust the screen. Check the database itself.

Install a SQLite viewer (for example [DB Browser for SQLite](https://sqlitebrowser.org/)),
open `timefold.db`, and run:

```sql
SELECT datetime(started_at_utc_ms / 1000, 'unixepoch', 'localtime') AS started,
       app_name,
       window_title
FROM activity_records
ORDER BY started_at_utc_ms DESC
LIMIT 20;
```

This matters most when testing the privacy gate. "The app does not show it" is
not proof. "It is not in the database" is proof.

---

## Project layout

```
TimeFold/
├── PHASES.md                      the 10-phase plan and your decisions
├── COMPONENTS.md                  what the parts are and how they connect
├── TimeFold.sln
│
├── src/
│   ├── TimeFold.Core/             all the logic. Knows nothing about screens.
│   │   ├── Models/                the shape of the data
│   │   ├── Time/                  UTC and timezone handling
│   │   ├── Logging/               log files
│   │   ├── Storage/               the database and migrations
│   │   ├── Settings/              saved settings
│   │   ├── Privacy/               the privacy gate  <- most important folder
│   │   ├── Watchers/              watching the active window
│   │   │   └── Native/            the Windows API calls
│   │   ├── Recording/             joins watcher + gate + store together
│   │   └── SystemIntegration/     Windows startup entry
│   │
│   └── TimeFold.App/              the visible part
│       ├── Composition/           AppHost - where everything is created
│       ├── Tray/                  the icon near the clock
│       ├── Views/                 the window
│       └── Assets/                the icon file
│
├── installer/                     Inno Setup script
└── docs/                          this file
```

**The rule that keeps this clean:** `TimeFold.Core` must never reference
`TimeFold.App`. The compiler enforces it. If you ever need to break that rule to
add a feature, something has been put in the wrong place.

---

## Reading the code for the first time

Start here, in this order:

1. `src/TimeFold.App/Composition/AppHost.cs` — read `Start()` top to bottom.
   It shows the whole boot sequence in 9 numbered steps.
2. `src/TimeFold.Core/Watchers/WindowWatcher.cs` — how one record is built.
3. `src/TimeFold.Core/Privacy/PrivacyGate.cs` — the rules about what is saved.
4. `src/TimeFold.Core/Recording/RecordingService.cs` — how those two connect.

## Running the tests

  Determining projects to restore...
  Restored d:\TimeFold\src\TimeFold.App\TimeFold.App.csproj (in 403 ms).
  Restored d:\TimeFold\src\TimeFold.Core\TimeFold.Core.csproj (in 404 ms).
  1 of 3 projects are up-to-date for restore.
  TimeFold.Core -> d:\TimeFold\src\TimeFold.Corein\Debug
et8.0-windows\TimeFold.Core.dll
  TimeFold.Core.Tests -> d:\TimeFold	ests\TimeFold.Core.Testsin\Debug
et8.0-windows\TimeFold.Core.Tests.dll
Test run for d:\TimeFold	ests\TimeFold.Core.Testsin\Debug
et8.0-windows\TimeFold.Core.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.2 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    48, Skipped:     0, Total:    48, Duration: 65 ms - TimeFold.Core.Tests.dll (net8.0)

48 tests, all in `tests/TimeFold.Core.Tests`. They need no database, no window and no Windows
APIs - they test plain functions. The important ones are in `Privacy/PrivacyGateTests.cs`:
that class decides what is allowed to be written to disk, so it is the one place where a bug
cannot be undone afterwards.

Run them before every commit, and whenever you add a rule to the block list.

## The web interface

Since the UI moved to a web page, `src/TimeFold.Web/` holds a small HTTP server
and the page itself:

```
src/TimeFold.Web/
  WebServer.cs      the server, and the four rules that keep the port safe
  ApiHandler.cs     everything the page can ask for
  StaticFiles.cs    serves the page, and refuses to serve anything outside it
  wwwroot/
    index.html      the page
    style.css       every colour and size, named once at the top
    app.js          plain JavaScript, no framework, no build step
```

Nothing here needs installing or building. Change `style.css`, reload the page
in the browser, and you see the change - the files are read from disk at
runtime, not baked into the exe.

**The server is not running most of the time.** It starts when you choose
"Open TimeFold" from the tray icon, on a random port, with a fresh random key.
If you never open the interface, no port is ever opened.

To see the address while it is running, look in the log:

```
%LOCALAPPDATA%\TimeFold\logs	imefold-<date>.log
```

The port is logged. The key deliberately is not - a log file is plain text that
gets copied around and pasted into bug reports.
