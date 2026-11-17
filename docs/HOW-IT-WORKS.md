# How TimeFold works (Phase 1)

**Read one section at a time.** Sections 1 to 3 explain the technologies.
Section 4 maps folders to purposes. Section 5 walks through one second of the
app running, which is the fastest way to understand the whole thing.

---

## 1. Every technology used so far

| Technology | Where it is used | Why this one |
|---|---|---|
| **C# 12 / .NET 8** | everywhere | Windows APIs are one line of code in C#. One language for logic and UI |
| **WPF** (+ XAML) | the window | Microsoft's desktop UI framework. XAML is the layout, C# is the behaviour |
| **Windows Forms** | the tray icon only | WPF has no tray icon. `NotifyIcon` from Windows Forms is the standard answer |
| **P/Invoke** | reading the active window | The bridge that lets C# call functions inside Windows itself |
| **Win32 API** (`user32.dll`, `kernel32.dll`) | reading the active window | The actual Windows functions. There is no higher-level way to do this |
| **SQLite** | storing everything | A database that is one file, with no server to run |
| **Microsoft.Data.Sqlite** | talking to SQLite | The official .NET driver for SQLite. **The only NuGet package in the project** |
| **SQL** (written by hand) | all database access | No ORM. You can read every query and see exactly what it does |
| **WAL mode** | SQLite setting | Lets us read and write at the same time, and survives a power cut |
| **Windows Registry** | start with Windows | One value under `HKEY_CURRENT_USER`. No admin rights needed |
| **SHA-256** | building record ids | Turns a record's own data into a fixed id, so saving twice is harmless |
| **`System.Threading.Timer`** | the 1-second check | Runs on a background thread, so it keeps working with no window open |
| **`DispatcherTimer`** | refreshing the window | The WPF version of a timer. Runs on the screen thread, which WPF requires |
| **`Mutex`** | only one copy runs | A Windows-wide lock. Two recorders would write duplicate records |
| **`FileVersionInfo`** | friendly app names | Turns `chrome.exe` into `Google Chrome` |
| **Inno Setup** | the installer | Free, makes a normal `.exe` installer, installs without admin rights |
| **dotnet CLI / MSBuild** | building | `dotnet build`, `dotnet publish` |
| **Git** | version control | — |

That is the whole list. Deliberately short: **one** NuGet package, no frameworks
we do not need, nothing to install on the user's machine.

---

## 2. How TimeFold asks Windows what you are doing

This is the heart of Phase 1. It happens in
[ForegroundWindowReader.cs](../src/TimeFold.Core/Watchers/Native/ForegroundWindowReader.cs).

### First: what is P/Invoke?

Windows itself is written in C, and its functions live inside files like
`user32.dll`. C# cannot call them directly. **P/Invoke** ("Platform Invoke") is
the bridge. You declare the function and tell .NET which DLL it lives in:

```csharp
[DllImport("user32.dll")]
internal static extern IntPtr GetForegroundWindow();
```

Reading that line:

| Part | Meaning |
|---|---|
| `[DllImport("user32.dll")]` | "This function is inside user32.dll, go find it" |
| `extern` | "I am not writing the body. It is somewhere else" |
| `IntPtr` | A raw memory address or handle. Really just a number |

All of these declarations are in one file,
[NativeMethods.cs](../src/TimeFold.Core/Watchers/Native/NativeMethods.cs),
and that file contains **no logic at all**. All the awkward Windows detail is
kept in one small place.

### The 8 steps, every second

```
  1. GetForegroundWindow()
         -> a window handle (just a number, e.g. 0x000504F2)
         |
  2. GetWindowTextLength(handle)
         -> how many characters the title has
         |
  3. GetWindowText(handle, buffer, size)
         -> "TimeFold.exe - TimeFold - Visual Studio Code"
         |
  4. GetWindowThreadProcessId(handle, out processId)
         -> 18504   (which running program owns that window)
         |
  5. OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId)
         -> a process handle, so we may ask questions about it
         |
  6. QueryFullProcessImageName(processHandle, ...)
         -> "C:\Users\...\Code.exe"
         |
  7. CloseHandle(processHandle)
         -> release it. Skipping this leaks memory, slowly, forever
         |
  8. FileVersionInfo.GetVersionInfo(exePath)
         -> "Visual Studio Code"   (the friendly name)
```

### Why these functions and not others

| Choice | Reason |
|---|---|
| `GetForegroundWindow` | The only API that answers "which window is the user actually in" |
| Two calls for the title (length, then text) | We must know the size before making a buffer to hold it |
| `CharSet = CharSet.Unicode` | Picks the `W` ("wide") version of each function, so emoji and non-English titles work. Your YouTube title had a 🎵 in it |
| `QueryFullProcessImageName`, not `Process.MainModule` | `MainModule` throws an error for many system programs. This one works far more often |
| `PROCESS_QUERY_LIMITED_INFORMATION` | The **smallest** permission that still returns the path. A bigger one would fail on protected programs and ask for more than we need |
| The app name is cached | `FileVersionInfo` reads the file from disk. The answer never changes for a given path, so we look it up once |

### What happens when Windows says no

Some programs are protected and `OpenProcess` fails. We then return no path,
and the Privacy Gate drops the record. That is the **fail closed** rule: if we
cannot identify the program, we cannot check it against the block list, so we
must not save it.

You have already seen this happen in your own log:

```
18:44:48 [INFO] Record dropped by privacy gate. Reason: empty_title
```

---

## 3. How the database is used

No ORM (no Entity Framework). Every query is hand-written SQL you can read.

**One table holds everything**, in every phase: `activity_records`. Later phases
add columns and new tables, never a second database.

Two settings matter more than they look, both in
[Database.cs](../src/TimeFold.Core/Storage/Database.cs):

| Setting | What it does |
|---|---|
| `journal_mode = WAL` | Writing and reading can happen at the same time, and an unexpected shutdown cannot corrupt the file |
| `synchronous = NORMAL` | Safe against the app crashing, with far fewer disk writes than `FULL` |

And one SQL trick does the "no duplicates" work, in
[SqliteActivityStore.cs](../src/TimeFold.Core/Storage/SqliteActivityStore.cs):

```sql
INSERT INTO activity_records (...) VALUES (...)
ON CONFLICT(id) DO UPDATE SET ...
```

"Insert this. If a row with that id already exists, update it instead."
Combined with ids built from the data itself, saving the same thing twice can
never create two rows. You saw this working: the record stayed at one row while
its length grew from 30s to 42s.

---

## 4. Folder structure, purpose by purpose

```
TimeFold/
│
├── PHASES.md            the 10-phase plan + your decisions
├── COMPONENTS.md        the parts and how they connect
├── docs/                this file, and BUILD.md
├── installer/           Inno Setup script
│
├── src/TimeFold.Core/   ALL THE LOGIC. Knows nothing about screens.
│   │
│   ├── Models/              The shape of the data. No behaviour.
│   ├── Time/                UTC + timezone. Every date goes through here.
│   ├── Logging/             Writing log files.
│   ├── Storage/             The database. The ONLY place with SQL.
│   │   └── Migrations/      Changing the database shape safely.
│   ├── Settings/            Saved settings (paused, device id).
│   ├── Privacy/             What may be saved.   <-- most important folder
│   ├── Watchers/            Watching what you do.
│   │   └── Native/          The Windows API calls. All of them, only here.
│   ├── Recording/           Joins watcher + gate + store together.
│   └── SystemIntegration/   Talking to Windows itself (startup entry).
│
└── src/TimeFold.App/    THE VISIBLE PART. Shows things, decides nothing.
    │
    ├── Composition/         AppHost - where every object is created.
    ├── Tray/                The icon near the clock.
    ├── Views/               The window.
    └── Assets/              The icon file.
```

### Why two projects instead of one

`TimeFold.Core` **cannot** reference `TimeFold.App`. The compiler refuses it.

That one restriction is doing a lot of work: it makes it impossible to
accidentally put logic in the UI, or to have the window read the database
directly. If we replace the UI in a later phase, or add a second one, none of
the logic moves.

### The rule for deciding where new code goes

Ask: **what is this code's job?**

| The code... | goes in |
|---|---|
| observes something about the computer | `Watchers/` |
| calls a Windows API | `Watchers/Native/` or `SystemIntegration/` |
| decides whether something may be saved | `Privacy/` |
| reads or writes the database | `Storage/` |
| connects other parts together | `Recording/` |
| shows something to the user | `App/Views/` or `App/Tray/` |

If a new feature needs you to edit three folders, something is in the wrong
place. That is a design warning, not a normal day.

---

## 5. Features built so far, and where each one lives

| # | Feature | Folder / file | Technology |
|---|---|---|---|
| 1 | Starts with Windows | `SystemIntegration/AutoStartService.cs` | Windows Registry |
| 2 | Runs invisibly in the tray | `App/Tray/TrayIconController.cs` | Windows Forms `NotifyIcon` |
| 3 | Only one copy can run | `App/App.xaml.cs` | `Mutex` |
| 4 | Sees the active window | `Watchers/Native/` | P/Invoke + Win32 API |
| 5 | Turns that into records | `Watchers/WindowWatcher.cs` | `System.Threading.Timer` |
| 6 | Decides what may be saved | `Privacy/` | plain C#, no files, no database |
| 7 | Saves records | `Storage/SqliteActivityStore.cs` | SQLite + SQL |
| 8 | Database can be upgraded later | `Storage/Migrations/` | SQL inside transactions |
| 9 | Remembers settings | `Settings/SettingsService.cs` | SQLite key/value table |
| 10 | Never saves a duplicate row | `Recording/RecordId.cs` | SHA-256 |
| 11 | Joins the parts together | `Recording/RecordingService.cs` | C# events |
| 12 | Times are always correct | `Time/AppClock.cs` | `DateTimeOffset` |
| 13 | Shows today's activity | `App/Views/MainWindow.xaml` | WPF + XAML |
| 14 | Writes a log | `Logging/FileLogger.cs` | plain file writing |
| 15 | Creates everything at startup | `App/Composition/AppHost.cs` | hand-written, no DI container |
| 16 | Makes an installer | `installer/TimeFold.iss` | Inno Setup *(not built yet)* |

---

## 6. One second in the life of TimeFold

This is the whole app in one trace. Follow the file names.

```
  The 1-second timer fires
  (WindowWatcher.cs, System.Threading.Timer, background thread)
        │
        ▼
  Ask Windows what is in front
  (ForegroundWindowReader.cs -> NativeMethods.cs -> user32.dll)
        │
        │   result: "Code.exe" + "TimeFold.exe - TimeFold - Visual Studio Code"
        ▼
  Is this the same as last second?
  (WindowWatcher compares exe path + title)
        │
        ├── SAME ──▶ just extend the current record's end time.
        │            Write to the database only every 30 seconds.
        │            (so a 3-hour session is ONE row, not 10,800)
        │
        └── DIFFERENT
                │
                ▼
          Finish the old record, start a new one
          (WindowWatcher.StartNewRecord -> RecordId.Create, SHA-256)
                │
                ▼
          PRIVACY GATE
          (PrivacyGate.cs - runs BEFORE anything is saved)
                │
                ├── paused?               ──▶ DROP
                ├── no program path?      ──▶ DROP  (fail closed)
                ├── empty title?          ──▶ DROP
                ├── on the block list?    ──▶ DROP
                │
                │   a DROP writes only a reason code to the log.
                │   never the title, never the path.
                │
                └── allowed
                        │
                        ▼
                  Save to SQLite
                  (SqliteActivityStore.Save -> INSERT ... ON CONFLICT)
                        │
                        ▼
                  Window refreshes (if open)
                  (MainWindow.xaml.cs, DispatcherTimer, every 5 seconds)
```

Two things to notice in that trace:

**The gate sits between watching and saving, not between saving and showing.**
Blocked data never reaches the disk. That is why the way to test it is to open
the database and find nothing — not to check that the screen hides it.

**The watcher runs on a background thread, the window on the screen thread.**
That is why recording continues perfectly with the window closed, and why
closing the window only hides it. Quit lives in the tray menu, and nowhere else.

---

## 7. What is deliberately NOT here yet

| Not built | Phase |
|---|---|
| Blocking apps you choose yourself | 2 |
| Not recording while idle or locked | 2 |
| Deleting and exporting your history | 2 |
| Search, and any date other than today | 3 |
| Website URLs | 4 |
| Screenshots | 5 |
| Reading text out of screenshots | 6 |
| Grouping into work sessions | 7 |
| Asking questions in plain language | 8 |
| Backup | 9 |

Phase 1 is only this: it installs, it runs, it records, it shows you. Everything
else is built on top of those four things working reliably.
