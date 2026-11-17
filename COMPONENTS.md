# TimeFold — Components and How They Connect

Companion to [PHASES.md](PHASES.md).

This file answers two questions: **what are the parts of TimeFold**, and **how do they talk to
each other**. Nothing here is about which language or library you use — that is decided per phase
in PHASES.md.

---

## 1. The whole system on one page

Data flows in one direction only: left to right.

```
  THINGS THAT WATCH            THE FILTER         THE STORE           THINGS THAT READ

  ┌──────────────────┐
  │ Window Watcher   │──┐
  │ (which app?)     │  │
  └──────────────────┘  │
  ┌──────────────────┐  │      ┌─────────┐      ┌──────────┐      ┌──────────────┐
  │ Idle/Lock Watcher│──┤      │         │      │          │      │  Question    │
  └──────────────────┘  │      │ PRIVACY │      │  STORE   │      │  Answerer    │
  ┌──────────────────┐  ├─────▶│  GATE   │─────▶│          │─────▶│  (searching) │
  │ Browser Reader   │──┤      │         │      │ SQLite + │      └──────┬───────┘
  └──────────────────┘  │      │ allow?  │      │  images  │             │
  ┌──────────────────┐  │      │ block?  │      │          │      ┌──────▼───────┐
  │ Screenshot Taker │──┘      └────┬────┘      └────┬─────┘      │    SCREEN    │
  └──────────────────┘              │                │            │   (the UI)   │
                                    │                │            └──────────────┘
                              blocked data      ┌────▼─────┐
                              is thrown away    │ WORKERS  │
                              and never saved   │ OCR      │
                                                │ Sessions │
                                                │ Backup   │
                                                └──────────┘
```

Read it as a sentence:

> Watchers see something → the Gate decides whether it is allowed → the Store saves it → the
> Workers improve it → the Screen and the Question Answerer read it.

---

## 2. The parts

### Group A — Things that watch (collectors)

These only watch and report. They never save anything themselves, and they never decide what is
private.

| Component | What it does | Arrives in |
|---|---|---|
| **Window Watcher** | Notices which app and window you are using, and for how long | Phase 1 |
| **Idle / Lock Watcher** | Notices when you stop typing, and when Windows locks | Phase 2 |
| **Browser Reader** | Reads which websites you visited | Phase 4 |
| **Screenshot Taker** | Takes a picture of your screen when something changes | Phase 5 |

Each one reports the same way (see section 4), so adding a new watcher later never changes anything
downstream.

---

### Group B — The filter

| Component | What it does | Arrives in |
|---|---|---|
| **Privacy Gate** | Looks at everything before it is saved and decides: allow it, hide part of it, or throw it away | Phase 1 (simple) → Phase 2 (real) |

**This is the most important component in TimeFold.** Three rules:

1. It runs **before** saving, not before showing. Blocked data is never written to disk at all.
2. If it is not sure, it throws the data away. Losing one boring record is fine. Saving one private
   record is not.
3. It writes *that* something was blocked (a count, a reason) but **never what** was blocked. A log
   file that records the blocked content has defeated the gate.

---

### Group C — The store

| Component | What it does | Arrives in |
|---|---|---|
| **Store** | The only component allowed to touch the database. Saves records, finds records, deletes records | Phase 1 |
| **Image Store** | Screenshot files on disk, organised by date | Phase 5 |

Everything else asks the Store to do things. Nobody else writes SQL. This is what makes it possible
to replace the database later without touching the rest of the app.

**Deleting is the Store's job, all in one step.** When you delete a day, the Store removes the
record, the search entry, and the image file together. If deleting were spread across components,
you would eventually end up with a "deleted" item still showing in search — which, in a privacy
app, is a serious bug.

---

### Group D — Workers (slow background jobs)

These read what the Store already has, and add to it. They are all optional — if any of them stops,
recording continues normally.

| Component | What it does | Arrives in |
|---|---|---|
| **OCR Worker** | Reads the text inside saved screenshots so you can search it | Phase 6 |
| **Session Builder** | Groups related records into work sessions | Phase 7 |
| **Retention Worker** | Deletes old screenshots when the disk limit is reached | Phase 5 |
| **Backup Worker** | Encrypts and uploads a backup | Phase 9 |

Workers must always give way to the watchers. The app must never feel slow because a worker is
busy.

---

### Group E — Things that read

| Component | What it does | Arrives in |
|---|---|---|
| **Search / Query** | Finds records by time, app, website, keyword | Phase 1 (basic) → Phase 3 (real) |
| **Screen (the UI)** | Timeline, search box, screenshot viewer, settings | Phase 1 |
| **Question Answerer** | Turns your plain-language question into searches, then into an answer | Phase 8 |

The UI talks only to Search and Settings. It never reads the database and never calls Windows APIs.
That is what makes the UI replaceable.

The Question Answerer is **just another user of Search**. It gets no special access to your data.
This is the reason it can only answer from real records: there is no other path for it to get
anything.

---

## 3. Who is allowed to talk to whom

| From | May talk to | May NOT talk to |
|---|---|---|
| Watchers | the Privacy Gate | the Store, the database, each other |
| Privacy Gate | the Store | Windows APIs, the UI |
| Store | the database and image files | the Watchers |
| Workers | the Store | the Watchers, the UI |
| Search | the Store | the Watchers |
| UI | Search, Settings | the Store, the database, Windows APIs |
| Question Answerer | Search, the AI provider | the Store, the database, image files |

**If you ever need to break one of these rules to add a feature, stop and redesign.** That rule
being in the way means two components have grown into each other, and from that point every change
starts costing twice as much.

---

## 4. How watchers report (the one shared format)

Every watcher, in every phase, reports the same shape. New kinds of data add new values of `kind`
and new contents inside `details` — they do not add new fields at the top.

```
Record {
  id          : a code built from the data itself (not random)
  kind        : "window" | "website" | "screenshot" | "idle" | "lock" ...
  from        : which watcher sent it
  started_at  : UTC time
  ended_at    : UTC time (empty for one-moment events)
  tz_offset   : your timezone offset at that moment
  details     : the parts specific to this kind
  saved_at    : when it was actually written
}
```

Three small details that are easy to add now and painful to add later:

- **`id` is built from the data, not random.** If the same thing is seen twice (after a crash, or
  during a Phase 9 backup restore), it produces the same `id`, so you get one record instead of two.
- **`tz_offset` is saved on every record.** Without it, "what was I doing at 3 PM?" silently gives
  wrong answers after a clock change or a trip to another country. Saving UTC alone is not enough.
- **`saved_at` is separate from `started_at`.** In Phase 4 you import browser history from the past,
  and in Phase 6 you add OCR text later. Without two separate times you cannot tell what actually
  happened when.

---

## 5. Where things live on disk

```
C:\Users\<you>\AppData\Local\TimeFold\
│
├── timefold.db          the database — all records and search
├── screenshots\         images, in folders by year\month\day     (Phase 5)
├── thumbnails\          small previews for fast scrolling        (Phase 5)
├── logs\                what the app did (never what you did)
└── backups\             encrypted backup files                   (Phase 9)
```

And the code:

```
TimeFold\
├── PHASES.md
├── COMPONENTS.md
├── src\
│   ├── watchers\      window, idle, browser, screenshot
│   ├── privacy\       the Gate  ← the most thoroughly tested folder in the project
│   ├── store\         database, saving, finding, deleting
│   ├── workers\       OCR, sessions, retention, backup
│   ├── search\        queries and filters
│   ├── ai\            question answering                 (Phase 8)
│   └── ui\            the screen — no database, no Windows APIs
└── installer\
```

The folders match the components on purpose. If adding one small feature forces you to edit three
folders, something has gone wrong in the design.

---

## 6. How each component grows, phase by phase

| Component | P1 | P2 | P3 | P4 | P5 | P6 | P7 | P8 | P9 | P10 |
|---|---|---|---|---|---|---|---|---|---|---|
| Window Watcher | **new** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Idle / Lock Watcher | | **new** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Privacy Gate | **new** | grows | ✓ | grows | grows | ✓ | ✓ | ✓ | ✓ | ✓ |
| Store | **new** | grows | grows | grows | grows | grows | grows | ✓ | grows | ✓ |
| Search | basic | | **real** | grows | | grows | ✓ | ✓ | ✓ | ✓ |
| UI | basic | grows | grows | grows | grows | ✓ | grows | grows | grows | grows |
| Browser Reader | | | | **new** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Screenshot Taker | | | | | **new** | ✓ | ✓ | ✓ | ✓ | ✓ |
| Retention Worker | | | | | **new** | ✓ | ✓ | ✓ | ✓ | ✓ |
| OCR Worker | | | | | | **new** | ✓ | ✓ | ✓ | ✓ |
| Session Builder | | | | | | | **new** | ✓ | ✓ | ✓ |
| Question Answerer | | | | | | | | **new** | ✓ | ✓ |
| Backup Worker | | | | | | | | | **new** | ✓ |

Notice that the first three components, built in Phases 1 and 2, are never replaced. Everything
else is added beside them. That is the point of this shape: you are not going to have to rebuild
the middle of the app in Phase 7.

---

## 7. Five rules that apply everywhere

1. **One process, running as you.** Not a Windows Service — a service cannot see your desktop, so
   it could not read the active window or take a screenshot at all.
2. **Always save UTC time plus your timezone offset.** Never save local time by itself.
3. **If an expensive part breaks, the cheap part keeps working.** Screenshots, OCR, AI and backup
   can all fail without stopping app recording. This is why they are separate components and not
   one big one.
4. **Show problems, do not hide them.** "Browser tracking stopped 3 days ago" should be visible in
   the app. In a memory product, a silent gap is worse than a visible one — you would trust the
   history without knowing part of it is missing.
5. **Facts and guesses are stored separately.** "You opened this GitHub issue" is a fact.
   "You were fixing the login bug" is a guess. They must never be stored in the same field or shown
   the same way, or you will stop being able to tell which is which.
