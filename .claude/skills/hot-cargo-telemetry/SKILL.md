---
name: hot-cargo-telemetry
description: Pull and read Hot Cargo telemetry after a playtest or a test run, and turn the findings into tunable changes or bug reports. Use after the player plays a session, after a long test run, or when a balance question needs real numbers.
---

# Telemetry

Files, events and report options are in `TELEMETRY.md`.

## Pull and report

1. Pull: `scripts/pull-telemetry.sh [SLOT...]`. It only reads files, so it is safe while the player plays. For the player's sessions only: `scripts/pull-telemetry.sh main`.
2. Report: `cd tools/telemetry && uv run hc-telemetry`. For the player's newest run: `uv run hc-telemetry --no-autotest --latest 1`. It writes `../.telemetry/report/report.md` and `report.html`.
3. Read the findings at the top of `report.md` first, then the sections of each run.
4. For a question that the report does not answer, read the events: `jq -c 'select(.e=="<event>")' <file>.jsonl`.

## During a playtest

- Before play starts, install the newest green `main` on every player's machine (`hot-cargo-install`), after each merge. Both players need the same version.
- When play starts, follow the host's live notes file (`tail -F`, path in `TELEMETRY.md`, Playtest notes) and act on each note at once: triage it, and queue a fix to an agent. Do not wait for the end of the session. Reason: the player asked for it, and notes lose their context fast.
- A fix made during the session installs only at a break, on every machine together.
- "Where is X?": run `hc-where` on the host's telemetry folder (`TELEMETRY.md`, Positions). Its positions can be up to 30 game seconds old.

## What to look at

- Errors: `log` events with level error, invariant breaks, `mp.desync`. Each one is a bug.
- Heat by source and the stage starts: compare with the heat budget in `BALANCE.md` (Findings, 4).
- Each sector visit (stay, missions, hunters, sales): compare with the model (6 missions and 17.5 minutes in each sector).
- Money and fame by source, power and money over time: compare with `BALANCE.md` (Findings, 2).
- Tech changes by cause: a cause `other` means a tech change with no hint. Find out why.
- Hunter outcomes and damage by side.
- Perf: the telemetry share of real time must stay well below 1 %. Frame times count only from a game with graphics, not from a headless test.

## Turn findings into work

- A value that feels wrong to the player: change it in the running game only when the player asks (`hot-cargo-tune`).
- A default that is wrong: change it in the repo (`hot-cargo-balance`).
- A bug: write a test that fails first (`hot-cargo-testing`), then fix it in a PR (`hot-cargo-pr-flow`).
- A question that no event answers: add an event (`TELEMETRY.md`, "Add an event").
- Give each finding the run ID, the numbers and the report section, so others can check it.
