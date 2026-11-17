/* ==========================================================================
   TimeFold - the web interface.

   Plain JavaScript on purpose. No framework, no build step, nothing to
   install. This whole app is a list, a search box and two settings screens;
   a framework would add more machinery than it saves, and this way you can
   read the file top to bottom and know what happens.

   One rule followed everywhere below: build the page with createElement and
   textContent, never by sticking strings of HTML together. Window titles come
   from other people's programs, and a title containing < or " would otherwise
   be treated as part of the page. textContent cannot do that - it puts text in
   as text, always.
   ========================================================================== */

"use strict";

/* --------------------------------------------------------------------------
   The secret key.

   It arrives in the address, and the server will not answer without it. We
   take it out of the address bar straight away so it does not sit in your
   browser history or get copied when you share a link.
   -------------------------------------------------------------------------- */
const KEY = new URLSearchParams(location.search).get("key") || "";

if (KEY) {
  history.replaceState(null, "", location.pathname);
}

/* --------------------------------------------------------------------------
   Talking to TimeFold
   -------------------------------------------------------------------------- */

async function api(path, body) {
  const options = {
    method: body === undefined ? "GET" : "POST",
    headers: { "X-TimeFold-Key": KEY }
  };

  if (body !== undefined) {
    options.headers["Content-Type"] = "application/json";
    options.body = JSON.stringify(body);
  }

  const response = await fetch(path, options);

  if (!response.ok) {
    throw new Error("TimeFold said no (" + response.status + ").");
  }

  return response.json();
}

/* --------------------------------------------------------------------------
   Small helpers
   -------------------------------------------------------------------------- */

const $ = (id) => document.getElementById(id);

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = text;
  return node;
}

function clear(node) {
  while (node.firstChild) node.removeChild(node.firstChild);
}

let toastTimer = null;

function toast(message) {
  const node = $("toast");
  node.textContent = message;
  node.classList.add("is-shown");

  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => node.classList.remove("is-shown"), 2600);
}

/**
 * Asks a yes/no question and waits for the answer.
 *
 * Deliberately not window.confirm: that is a browser dialog that looks
 * nothing like the rest of the page, and some browsers let you silence it -
 * which would mean a delete happening with no question asked at all.
 */
function ask(title, message, confirmText) {
  return new Promise((resolve) => {
    $("modalTitle").textContent = title;
    $("modalBody").textContent = message;
    $("modalConfirm").textContent = confirmText || "Yes";
    $("modalBackdrop").classList.add("is-shown");

    const finish = (answer) => {
      $("modalBackdrop").classList.remove("is-shown");
      $("modalConfirm").onclick = null;
      $("modalCancel").onclick = null;
      resolve(answer);
    };

    $("modalConfirm").onclick = () => finish(true);
    $("modalCancel").onclick = () => finish(false);
  });
}

function formatSize(bytes) {
  if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + " KB";
  return (bytes / 1024 / 1024).toFixed(1) + " MB";
}

function formatTotal(seconds) {
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.round((seconds % 3600) / 60);
  if (hours === 0) return minutes + "m";
  return hours + "h " + minutes + "m";
}

function toDayInput(date) {
  // The yyyy-MM-dd that a date input wants, built from the LOCAL date.
  // toISOString would convert to UTC first, which quietly shows the wrong day
  // for anyone east or west of Greenwich - including here.
  const pad = (n) => String(n).padStart(2, "0");
  return date.getFullYear() + "-" + pad(date.getMonth() + 1) + "-" + pad(date.getDate());
}

function describeDay(dayText) {
  const today = toDayInput(new Date());
  const yesterday = toDayInput(new Date(Date.now() - 86400000));

  if (dayText === today) return "Today";
  if (dayText === yesterday) return "Yesterday";

  return new Date(dayText + "T12:00:00").toLocaleDateString(undefined, {
    day: "numeric", month: "long", year: "numeric"
  });
}

/* --------------------------------------------------------------------------
   What is on screen right now
   -------------------------------------------------------------------------- */

const state = {
  view: "timeline",
  day: toDayInput(new Date()),
  search: "",
  apps: [],
  appSearch: "",
  blockedOnly: false
};

/* --------------------------------------------------------------------------
   Moving between views
   -------------------------------------------------------------------------- */

document.querySelectorAll(".nav-item").forEach((button) => {
  button.onclick = () => showView(button.dataset.view);
});

function showView(name) {
  state.view = name;

  document.querySelectorAll(".nav-item").forEach((b) =>
    b.classList.toggle("is-active", b.dataset.view === name));

  document.querySelectorAll(".view").forEach((v) =>
    v.classList.toggle("is-active", v.id === "view-" + name));

  if (name === "timeline") loadTimeline();
  if (name === "apps") loadApps();
  if (name === "data") loadState();
}

/* ==========================================================================
   THE TIMELINE
   ========================================================================== */

$("prevDay").onclick = () => goToDay(addDays(state.day, -1));
$("nextDay").onclick = () => goToDay(addDays(state.day, 1));
$("todayButton").onclick = () => goToDay(toDayInput(new Date()));
$("dayPicker").onchange = () => goToDay($("dayPicker").value);

function addDays(dayText, days) {
  const date = new Date(dayText + "T12:00:00");
  date.setDate(date.getDate() + days);
  return toDayInput(date);
}

function goToDay(dayText) {
  if (!dayText) return;

  state.day = dayText;

  // Choosing a day means you want to see that day, so a search left in the
  // box would hide the thing you just asked for.
  if (state.search) {
    state.search = "";
    $("searchBox").value = "";
  }

  $("dayPicker").value = dayText;
  loadTimeline();
}

/* Searching waits until you stop typing. Every keystroke firing a request
   would make the list flicker and would search for half-typed words. */
let searchTimer = null;

$("searchBox").oninput = () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => {
    state.search = $("searchBox").value.trim();
    loadTimeline();
  }, 180);
};

async function loadTimeline() {
  const body = $("timelineBody");
  const searching = state.search.length > 0;

  try {
    const data = searching
      ? await api("/api/search?q=" + encodeURIComponent(state.search))
      : await api("/api/day?date=" + state.day);

    $("dayHeading").textContent = searching ? "Search" : describeDay(state.day);

    $("daySubheading").textContent = searching
      ? "Every day you have ever recorded."
      : new Date(state.day + "T12:00:00").toLocaleDateString(undefined, {
          weekday: "long", day: "numeric", month: "long", year: "numeric" });

    const stats = $("dayStats");
    clear(stats);
    stats.appendChild(el("b", null, String(data.entries.length)));
    stats.appendChild(document.createTextNode(
      searching ? " results · " : " entries · "));
    stats.appendChild(el("b", null, formatTotal(data.totalSeconds)));
    stats.appendChild(document.createTextNode(" recorded"));

    drawEntries(body, data.entries, searching);
  } catch (error) {
    clear(body);
    body.appendChild(showEmpty("Could not load", error.message));
  }
}

function showEmpty(title, detail) {
  const box = el("div", "empty");
  box.appendChild(el("strong", null, title));
  box.appendChild(el("div", null, detail));
  return box;
}

function drawEntries(container, entries, searching) {
  clear(container);

  if (entries.length === 0) {
    container.appendChild(showEmpty(
      searching ? "Nothing found" : "Nothing recorded on this day",
      searching
        ? "Try a different word, or part of a word."
        : "If TimeFold was not running, there is nothing to show."));
    return;
  }

  // Group by the hour, which is what turns a log into a timeline.
  let currentGroup = null;
  let list = null;

  entries.forEach((entry) => {
    if (entry.group !== currentGroup) {
      currentGroup = entry.group;

      const block = el("div", "hour-block");
      const head = el("div", "hour-head");

      head.appendChild(el("span", "hour-time", entry.group));
      head.appendChild(el("span", "hour-count", ""));

      block.appendChild(head);

      list = el("div", "entries");
      block.appendChild(list);
      container.appendChild(block);
    }

    list.appendChild(drawOneEntry(entry, searching));

    // Keep the count in the heading honest as rows are added.
    const count = list.parentElement.querySelector(".hour-count");
    const n = list.children.length;
    count.textContent = n === 1 ? "1 entry" : n + " entries";
  });
}

function drawOneEntry(entry, searching) {
  const isAway = entry.kind === "idle" || entry.kind === "lock";

  const row = el("div", "entry" + (isAway ? " is-away" : ""));

  row.appendChild(el("div", "entry-time", entry.startText + " – " + entry.endText));
  row.appendChild(el("div", "entry-length", entry.durationText));

  const what = el("div", "entry-what");

  if (searching) {
    what.appendChild(el("span", "day-chip", entry.dayText));
  }

  what.appendChild(el("span", "entry-app", entry.appName || "Unknown"));

  if (!isAway) {
    what.appendChild(el("span", "entry-title", entry.windowTitle || ""));
  }

  row.appendChild(what);

  // Being away is not something you can block or usefully act on.
  if (!isAway) {
    row.oncontextmenu = (event) => {
      event.preventDefault();
      openRowMenu(event, entry);
    };
  }

  return row;
}

/* --------------------------------------------------------------------------
   The right-click menu on a row
   -------------------------------------------------------------------------- */

let menuEntry = null;

function openRowMenu(event, entry) {
  menuEntry = entry;

  const menu = $("rowMenu");
  menu.classList.add("is-shown");

  // Keep it on screen when you click near the right or bottom edge.
  const width = menu.offsetWidth;
  const height = menu.offsetHeight;

  menu.style.left = Math.min(event.clientX, window.innerWidth - width - 8) + "px";
  menu.style.top = Math.min(event.clientY, window.innerHeight - height - 8) + "px";
}

document.addEventListener("click", () => $("rowMenu").classList.remove("is-shown"));

$("rowMenu").querySelectorAll("button").forEach((button) => {
  button.onclick = () => handleRowAction(button.dataset.action);
});

async function handleRowAction(action) {
  const entry = menuEntry;

  if (!entry) return;

  if (action === "delete-entry") {
    const yes = await ask(
      "Delete this entry?",
      entry.startText + " – " + entry.endText + "\n" +
      (entry.appName || "") + "\n" + (entry.windowTitle || "") +
      "\n\nThis cannot be undone.",
      "Delete");

    if (!yes) return;

    await api("/api/records/delete", { id: entry.id });
    toast("Entry deleted.");
    loadTimeline();
    return;
  }

  if (!entry.exeFileName) {
    toast("This entry does not belong to a program.");
    return;
  }

  if (action === "delete-app") {
    const yes = await ask(
      "Delete everything from " + entry.appName + "?",
      "Every day, not just this one. This cannot be undone.",
      "Delete");

    if (!yes) return;

    const result = await api("/api/records/delete-app", { exeFileName: entry.exeFileName });
    toast("Deleted " + result.deleted + " entries.");
    loadTimeline();
    return;
  }

  if (action === "block-app") {
    const yes = await ask(
      "Never record " + entry.appName + " again?",
      "Nothing from " + entry.exeFileName + " will be written to the database from now on.",
      "Block it");

    if (!yes) return;

    const result = await api("/api/apps/block", {
      exeFileName: entry.exeFileName, blocked: true
    });

    await offerToDeletePast(entry.appName, entry.exeFileName, result.pastRecords);

    toast(entry.appName + " is blocked.");
    loadTimeline();
  }
}

/**
 * Blocking only stops NEW records. Someone who has just decided they do not
 * want an app in their history would be surprised to find the old entries
 * still sitting there - so we ask, rather than assume either way.
 */
async function offerToDeletePast(appName, exeFileName, pastRecords) {
  if (!pastRecords) return;

  const yes = await ask(
    "Delete what it already recorded?",
    appName + " is now blocked, so nothing new will be recorded.\n\n" +
    "There are still " + pastRecords + " entries from before. Delete those too?\n\n" +
    "This cannot be undone.",
    "Delete them");

  if (!yes) return;

  const result = await api("/api/records/delete-app", { exeFileName: exeFileName });
  toast("Deleted " + result.deleted + " past entries.");
}

/* ==========================================================================
   BLOCKED APPS
   ========================================================================== */

$("appSearch").oninput = () => {
  state.appSearch = $("appSearch").value.trim().toLowerCase();
  drawApps();
};

$("blockedOnly").onchange = () => {
  state.blockedOnly = $("blockedOnly").checked;
  drawApps();
};

$("rescanApps").onclick = () => loadApps(true);

async function loadApps(rescan) {
  const body = $("appsBody");

  clear(body);
  body.appendChild(showEmpty("Looking for your apps", "This takes a moment."));

  const data = await api("/api/apps" + (rescan ? "?rescan=1" : ""));
  state.apps = data.apps;

  drawApps();
  loadRules();
}

function drawApps() {
  const body = $("appsBody");

  clear(body);
  body.appendChild(buildHeadRow(["", "App", "Program file", "Found in", "Status"]));

  const shown = state.apps.filter((app) => {
    if (state.blockedOnly && !app.blocked) return false;

    if (state.appSearch) {
      const haystack = (app.displayName + " " + app.exeFileName).toLowerCase();
      if (!haystack.includes(state.appSearch)) return false;
    }

    return true;
  });

  if (shown.length === 0) {
    body.appendChild(showEmpty("No apps match", "Try a different word."));
    return;
  }

  shown.forEach((app) => body.appendChild(drawAppRow(app)));
}

function buildHeadRow(titles) {
  const row = el("div", "list-row list-head");
  titles.forEach((title) => row.appendChild(el("div", null, title)));
  return row;
}

function drawAppRow(app) {
  const row = el("div", "list-row");

  const toggle = el("label", "switch");
  const input = document.createElement("input");
  input.type = "checkbox";
  input.checked = app.blocked;
  input.onchange = () => setAppBlocked(app, input);

  toggle.appendChild(input);
  toggle.appendChild(el("span"));

  row.appendChild(toggle);
  row.appendChild(el("div", null, app.displayName));
  row.appendChild(el("div", "mono", app.exeFileName));
  row.appendChild(el("div", "subtle", app.foundIn));

  const status = el("div");
  if (app.blocked) {
    status.appendChild(el("span", "tag is-blocked",
      app.blockedByTimeFold ? "TimeFold" : "Blocked"));
  } else {
    status.appendChild(el("span", "tag is-recorded", "Recording"));
  }
  row.appendChild(status);

  return row;
}

async function setAppBlocked(app, input) {
  const wantBlocked = input.checked;

  // Unblocking something TimeFold blocks by default is allowed - it is your
  // computer - but it should be a deliberate act, not a stray click.
  if (!wantBlocked && app.blockedByTimeFold) {
    const yes = await ask(
      "Unblock " + app.displayName + "?",
      "TimeFold blocks this because it is a password manager or a Windows " +
      "sign-in screen.\n\nIts window titles will be recorded from now on.",
      "Unblock");

    if (!yes) {
      input.checked = true;
      return;
    }
  }

  const result = await api("/api/apps/block", {
    exeFileName: app.exeFileName, blocked: wantBlocked
  });

  app.blocked = result.blocked;

  if (wantBlocked) {
    await offerToDeletePast(app.displayName, app.exeFileName, result.pastRecords);
    toast(app.displayName + " is blocked.");
  } else {
    app.blockedByTimeFold = false;
    toast(app.displayName + " will be recorded again.");
  }

  drawApps();
  loadRules();
}

/* --------------------------------------------------------------------------
   Advanced rules
   -------------------------------------------------------------------------- */

const RULE_HELP = {
  ExeFileName:
    "The program's file name, for example KeePass.exe. Usually easier to do with the list above.",
  ExePathContains:
    "Any part of the program's full path, for example \\Banking\\. Blocks every program in a " +
    "folder at once. Careful: something like \\Program Files\\ would block almost everything.",
  WindowTitleContains:
    "Any word in the window title, for example Incognito or Payslip. The only way to block " +
    "private content inside an app you otherwise want recorded - but it over-blocks easily."
};

$("ruleType").onchange = updateRuleHelp;
function updateRuleHelp() { $("ruleHelp").textContent = RULE_HELP[$("ruleType").value]; }
updateRuleHelp();

$("addRule").onclick = async () => {
  const pattern = $("rulePattern").value.trim();

  if (!pattern) {
    toast("Type what to block first.");
    return;
  }

  // A very short pattern can match almost everything, which would quietly stop
  // TimeFold recording anything at all.
  if (pattern.length < 3) {
    const yes = await ask(
      "That is very short",
      "\"" + pattern + "\" may block far more than you expect.\n\nAdd it anyway?",
      "Add it");

    if (!yes) return;
  }

  await api("/api/rules", { matchType: $("ruleType").value, pattern: pattern });

  $("rulePattern").value = "";
  toast("Rule added.");
  loadRules();
  loadApps();
};

async function loadRules() {
  const data = await api("/api/rules");
  const body = $("rulesBody");

  clear(body);
  body.appendChild(buildHeadRow(["", "Matches on", "Pattern", "Note", "From"]));

  data.rules.forEach((rule) => {
    const row = el("div", "list-row rule-row");

    const toggle = el("label", "switch");
    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = rule.enabled;
    input.onchange = async () => {
      await api("/api/rules/enabled", { id: rule.id, enabled: input.checked });
      toast(input.checked ? "Rule switched on." : "Rule switched off.");
      loadApps();
    };

    toggle.appendChild(input);
    toggle.appendChild(el("span"));

    row.appendChild(toggle);
    row.appendChild(el("div", null, rule.matchTypeText));
    row.appendChild(el("div", "mono", rule.pattern));
    row.appendChild(el("div", "subtle", rule.note || ""));

    const from = el("div");

    if (rule.builtIn) {
      from.appendChild(el("span", "tag", "TimeFold"));
    } else {
      const remove = el("button", "btn", "Delete");
      remove.onclick = async () => {
        const yes = await ask(
          "Delete this rule?",
          rule.matchTypeText + ": " + rule.pattern +
          "\n\nAnything it was blocking will be recorded from now on.",
          "Delete");

        if (!yes) return;

        await api("/api/rules/delete", { id: rule.id });
        toast("Rule deleted.");
        loadRules();
        loadApps();
      };
      from.appendChild(remove);
    }

    row.appendChild(from);
    body.appendChild(row);
  });
}

/* ==========================================================================
   YOUR DATA
   ========================================================================== */

$("exportButton").onclick = () => {
  // A plain link download, so the browser saves the file wherever you keep
  // your downloads. The key has to travel in the address here, because a
  // download is not a fetch and cannot carry a header.
  const link = document.createElement("a");
  link.href = "/api/export?key=" + encodeURIComponent(KEY);
  link.download = "";
  link.click();

  toast("Export started.");
};

$("importFile").onchange = async () => {
  const file = $("importFile").files[0];

  if (!file) return;

  const text = await file.text();

  try {
    const result = await api("/api/import", { json: text });

    if (result.error) {
      toast(result.error);
    } else {
      toast("Restored " + result.restored + " entries.");
      loadState();
    }
  } catch (error) {
    toast(error.message);
  }

  $("importFile").value = "";
};

$("deleteDayButton").onclick = async () => {
  const day = $("deleteDayPicker").value;

  if (!day) {
    toast("Pick a date first.");
    return;
  }

  const yes = await ask(
    "Delete " + describeDay(day) + "?",
    "Everything recorded on that day will be removed.\n\nThis cannot be undone.",
    "Delete");

  if (!yes) return;

  const result = await api("/api/records/delete-day", { date: day });

  toast(result.deleted === 0
    ? "Nothing was recorded that day."
    : "Deleted " + result.deleted + " entries.");

  loadState();
};

$("deleteAllButton").onclick = async () => {
  const info = await api("/api/state");

  if (info.totalRecords === 0) {
    toast("There is nothing to delete.");
    return;
  }

  // Asked twice on purpose. The first question is easy to click through
  // without reading. The second one names what goes.
  const first = await ask(
    "Delete your entire history?",
    info.totalRecords + " entries will be removed.\n\n" +
    "If you have not exported a copy, say Cancel and do that first.",
    "Continue");

  if (!first) return;

  const second = await ask(
    "Last check",
    "All " + info.totalRecords + " entries, from every day, gone for good.\n\n" +
    "Your block rules and settings are kept.",
    "Delete everything");

  if (!second) return;

  const result = await api("/api/records/delete-all");

  toast("Deleted " + result.deleted + " entries.");
  loadState();
};

/* ==========================================================================
   Recording state
   ========================================================================== */

$("pauseButton").onclick = async () => {
  const info = await api("/api/state");
  const result = await api("/api/pause", { paused: !info.paused });

  showRecordingState(result.paused);
  toast(result.paused ? "Recording paused." : "Recording again.");
};

/* --------------------------------------------------------------------------
   Light and dark.

   The server already wrote the saved theme into the page before it was sent,
   so the first paint is correct. This only handles the button: switch the
   look straight away so it feels instant, then tell TimeFold to remember it.
   -------------------------------------------------------------------------- */

function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  $("themeButton").textContent = theme === "dark" ? "Light mode" : "Dark mode";
}

$("themeButton").onclick = async () => {
  const current = document.documentElement.getAttribute("data-theme");
  const next = current === "dark" ? "light" : "dark";

  applyTheme(next);

  try {
    await api("/api/theme", { theme: next });
  } catch (error) {
    toast("The look changed, but TimeFold could not remember it.");
  }
};

function showRecordingState(paused) {
  $("recordingLabel").textContent = paused ? "Paused" : "Recording";
  $("recordingPulse").classList.toggle("is-paused", paused);
  $("pauseButton").textContent = paused ? "Resume" : "Pause";
}

async function loadState() {
  const info = await api("/api/state");

  showRecordingState(info.paused);

  $("dataSummary").textContent =
    info.totalRecords + " entries, " + formatSize(info.databaseBytes) + " on disk.";

  $("dataPath").textContent = info.databasePath;

  // There is no history in the future, and none before your first record.
  $("dayPicker").max = info.today;
  $("deleteDayPicker").max = info.today;

  if (info.firstDay) {
    $("dayPicker").min = info.firstDay;
    $("deleteDayPicker").min = info.firstDay;
  }

  $("nextDay").disabled = state.day >= info.today;
}

/* ==========================================================================
   Keeping up to date
   ========================================================================== */

/*
 * Refresh every few seconds, but only when it would not get in your way.
 *
 * Redrawing throws away where you had scrolled to. That is fine while you
 * watch today fill up, and infuriating while you are reading last month or
 * typing in the search box - neither of which changes on its own anyway.
 */
setInterval(() => {
  if (document.hidden) return;
  if ($("modalBackdrop").classList.contains("is-shown")) return;

  loadState();

  if (state.view === "timeline" && !state.search && state.day === toDayInput(new Date())) {
    loadTimeline();
  }
}, 5000);

/* ==========================================================================
   Start
   ========================================================================== */

applyTheme(document.documentElement.getAttribute("data-theme"));

$("dayPicker").value = state.day;
$("deleteDayPicker").value = state.day;

loadState();
loadTimeline();
