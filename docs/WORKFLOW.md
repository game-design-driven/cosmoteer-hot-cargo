# Development workflow

This page is the map of how Hot Cargo is built: who works, where, and how a change reaches the player. It links to the detailed docs and skills and does not repeat them.

Each section has two parts:

- **Generic**: the pattern. It works for any project with parallel coding agents, a GitHub merge queue and tests that need a real machine.
- **Hot Cargo**: the parts that depend on this mod, Cosmoteer or the Windows PC.

## Overview

```
coordinator thread ── briefs ──► one agent per area (own worktree, own branch)
                                   │ local tests (unit, headless sim, PC)
                                   ▼
                                 PR ── CI on the PR: build, unit tests, spec numbers, test set check
                                   │ scripts/queue-pr.sh <n>
                                   ▼
                                 merge queue ── the PR's game test set in the headless sim
                                   ▼
                                 main ── full suite (safety net) ── release ── install into the player's game
```

## Agents and areas

Generic:
- A coordinator thread briefs the agents, watches them, judges failures and does the work that needs the player's machine. Rules and reasons: `.claude/skills/hot-cargo-coordinate/SKILL.md`.
- One agent owns one area and has a queue of its features. Two agents never work on features that touch the same files.
- Each agent saves its progress so that a restart loses nothing: a `PLAN.md` on its branch, a draft PR with a checklist, a push after each step that builds (`CLAUDE.md`, Agents).
- An agent ends with one message that starts with `FINAL REPORT`.
- Procedures live in skills (`.claude/skills/`). `CLAUDE.md` lists them. A skill gives the order of steps and links to the doc that holds the rules.

Hot Cargo:
- Beta: each run is a new save. Breaking old saves is fine. Agents write no save migration and no old-save compatibility code, and a PR does not list a save break as a risk (`CLAUDE.md`, Agents).
- An area is a folder `src/HotCargo/<Area>/` and `mod/<area>/` with its own entry point, tunables and rules (`CONTRIBUTING.md`, Layout; skill `hot-cargo-new-area`). Areas own separate files, so parallel work rarely edits the same file.
- The agents run as Claude threads in T3 Code. PC calls go over the T3 link and work only while the calling thread has an active turn (`CLAUDE.md`, Agents). Game tests run on the PC by themselves and need the link only for their start and for the results (`scripts/pc-suite.sh`).

## Branches and worktrees

Generic:
- The clone is a container folder: `.bare/` (the git repository), `_shared/` (local files that every worktree links), and one folder for each branch.
- Each agent works only in its own worktree: `git --git-dir ../.bare worktree add ../<name> -b <type>/<name> origin/main`.
- Branch names and commits use conventional commits (`feat/`, `fix/`, `docs/`, `ci/`, `test/`). Never credit an AI, a model or a tool.
- Local state that is not code lives next to `.bare/`, outside git:

| Folder | Holds |
|---|---|
| `.state/logs/<worktree>/` | full test logs |
| `.state/suites/` | logs of detached Mac runs (`scripts/detach.sh`) |
| `.state/flaky.log` | tests that passed only on their retry |
| `.state/player-hold` | the player hold (see "The player's machine") |
| `.state/installed_sha` | the commit in the player's game |
| `.state/slots.env` | the number of PC test slots |

Hot Cargo:
- `.game/` holds the game files and the decompiled game (`scripts/pull-game.sh`). `.game-refs/` is the clone of the private game files repository for CI (`scripts/refresh-game-refs.sh`). `.telemetry/` holds pulled telemetry.
- `_shared/scripts/pc.local.sh` names the PC (`PC_MACHINE`).
- The `play` worktree is a detached checkout of `origin/main` for installs (`scripts/install-player.sh`).

## PR flow and the merge queue

Generic:
- Every change goes through a PR. Nobody pushes to `main`.
- The agent that owns a PR merges it: mark it ready, then `scripts/queue-pr.sh <n>` (auto-merge, and the queue entry if `gh pr merge --auto` adds none). Only the coordinator uses `--admin`, and only for a known flake (`hot-cargo-coordinate`).
- `main` has a GitHub merge queue. It tests each PR on top of the newest `main` and the PRs ahead of it. A PR does not have to be up to date with `main`.
- `main` gets one merge commit for each PR, titled with the PR title. Read the history with `git log --first-parent --oneline`. Squash merges are off, because they broke stacked branches.
- PR text starts with 🤖, stays short, and shows images of visible changes.
- Steps, failures and queue traps: skill `hot-cargo-pr-flow`. Rules: `CONTRIBUTING.md`, Git.

Hot Cargo:
- Required checks: `build`, `spec-numbers`, `game-tests` (`.github/workflows/check.yml`).
- The PR template has the boxes for local tests and tests outside CI (`.github/pull_request_template.md`).

## Tests

Generic:
- Three kinds: unit tests for logic, rolls and math; game tests for what only the real game shows; manual playtests (`PLAYTEST.md`).
- Tiers: each area has one smoke test. A change runs the smoke tests of the areas it touches; the full tier runs every test (`tests/TIERS.md`).
- A new game test needs a reason: a critical, complex or bug-prone area (`tests/TIERS.md`, Rules).
- The area of a test comes from its file name, so no list maps areas to tests (`scripts/autotest-affected.sh`, header).
- Each failed test runs once more. A pass on the retry is FLAKY, not a failure.

Hot Cargo:
- Unit tests: `tests/HotCargo.Tests`, every `*Model.cs` file.
- Game tests: `tests/autotest/*.txt` and co-op sessions `tests/autotest-mp/*.txt`. They run in three places:

| Runner | Where | Script |
|---|---|---|
| headless sim | the Mac and CI (Linux) | `scripts/autotest-sim.sh`, `docs/headless-sim.md` |
| PC test slots | the Windows PC, headless | `scripts/autotest.sh`, `scripts/autotest-all.sh` |
| PC only | tests with the line `# runner: pc` | `LIST_ONLY=1 RUNNER=pc scripts/autotest-affected.sh` lists them |

- Before a push: `scripts/autotest-affected.sh` runs the affected tests in the sim and the PC-only ones on the PC. Details: skill `hot-cargo-testing`.

## CI test sets

Generic:
- A PR run builds and runs unit tests and checks, but runs no game test. The merge queue runs the PR's game test set.
- The default set comes from the changed files: none for docs, all for a change to the test harness, else the affected tests and a baseline.
- The PR text can change the set in a `### Game tests` block (`+ <test>`, `- <test>`, `all`, `none: <reason>`).
- A full suite runs on `main` at most 3 hours after a merge. It is the safety net for the small queue sets, and a release needs it green.

Hot Cargo:
- `scripts/ci-test-set.sh` (the set), `scripts/ci-change-kind.sh` (the kind of change), `scripts/ci-sim-tests.sh` (one shard), `.github/workflows/full-suite-trigger.yml` (the full suite after merges). The rules: `CONTRIBUTING.md`, Git.
- Game files for CI come from a private repository through a read-only deploy key. They never go into a cache or an artifact (`check.yml`, header).

## Flakes and failures

Generic:
- Before you call a failure unrelated, check the same test on `main`. A failure that shows only with the change belongs to the PR.
- A flaky or infrastructure failure in a test that the PR does not touch counts as passing. Note it in the PR in one line.
- Do not babysit a fragile test. Decide in one look if it is a bug or test variance, then move on.
- Steps: skill `hot-cargo-pr-flow`, "A failure that looks unrelated".

Hot Cargo:
- Infrastructure failure texts (the T3 relay, the PC memory, the deploy): skill `hot-cargo-testing`, "Infrastructure failures".

## The player's machine

Generic:
- The person who plays also owns the test machine. Their game slot is never started or stopped by an agent.
- A hold file blocks every script that could disturb the player. Only the coordinator clears it, and only when the player says the machine is free.
- Find a game process by its own data folder, never by a process id: ids get reused.

Hot Cargo:
- Slot `main` is the player's game. Slots `t1` to `tN` are headless test games with their own save folders (skill `hot-cargo-pc`).
- `scripts/player-hold.sh on|off|status`. While `.state/player-hold` exists, the scripts refuse slot `main` and windowed test slots (exit 3). Headless test slots stay allowed.
- Slot locks on the Mac (`/tmp/hot_cargo_slot_<n>.lock`) let many agents share the PC (skill `hot-cargo-pc`, Locks).

## Install and release

Generic:
- The player's game always runs the newest `main`. Install after each merge, never into a running game.
- Every push to `main` publishes an untested dev prerelease. A tested release takes the newest `main` commit with every required check and a green full suite, so releases need no merge freeze.

Hot Cargo:
- Install: `scripts/install-player.sh` (skill `hot-cargo-install`).
- Release: `scripts/release.sh <version>` (skill `hot-cargo-release`). Dev builds: `.github/workflows/dev-release.yml`.
- A new Cosmoteer version: skill `hot-cargo-game-update`.

## Spec numbering

Generic:
- The spec is one file per section in `spec/`, with numbered requirements that are unique across all files (`SPEC.md`).
- A change makes the spec lines it touches true in the same PR. Patch stale lines in place. Never renumber.
- Before you pick new numbers, check the open PRs. The coordinator gives parallel agents separate number blocks.
- CI fails when two files share a section number or two requirements share a number (`scripts/check-spec-numbers.sh`). In the merge queue this finds a clash between two PRs.

## Screenshots

Generic:
- An agent takes its own screenshots of a visible change and puts them in the PR. Never in the player's game.

Hot Cargo:
- `scripts/screens.sh` runs a script from `tests/screens/` in a windowed, minimized test slot, or on this Mac with `RUNNER=mac` (skill `hot-cargo-screenshots`). How it draws without focus: `UX.md`, Screenshots.
- Repo images go to `docs/screenshots/` as JPEG, with personal data cropped out.

## Public repo hygiene

Generic:
- No personal data in tracked files: no home paths, machine names, IDs or tokens. Machine settings go in an ignored local file.

Hot Cargo:
- No game files in git or in a release: no DLLs, no `Data/`, no decompiled code. CI reads them from the private game files repository.

## Coordinator tools

- `scripts/agent-watch.sh`: one line for each agent worktree (commits, pushes, dirty files, test runs, slots, refused PC calls).
- `scripts/player-hold.sh`: the player hold.
- Rules for briefs, merges, flakes and the player: skill `hot-cargo-coordinate`.

## Every script

Each file in `scripts/` and `.github/workflows/` starts with a header: what it does, its inputs, its exit codes and who calls it. Read the header before you run a script.
