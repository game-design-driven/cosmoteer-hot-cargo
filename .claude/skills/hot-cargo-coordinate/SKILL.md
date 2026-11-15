---
name: hot-cargo-coordinate
description: Coordinator work on Hot Cargo. Brief and watch the agents, set and clear the player hold, judge failures and admin merges, and run work in the player's game slot. Use when you coordinate several Hot Cargo agents, or when an agent asks for main-slot work.
---

# Coordinate

The whole workflow and which parts are generic: `docs/WORKFLOW.md`. Each rule below has its reason after "Reason:".

## Watch the agents

- Run `scripts/agent-watch.sh` from the `main` worktree, by hand or from a scheduled check. It prints one line for each agent worktree, and the count of held test slots and the main-slot lock. Its header names the columns.
- Signs that an agent needs a message:
  - No commit for a long time, with no running tests.
  - Commits that are not pushed (ahead).
  - Refused logs: the agent ran PC work without an active turn (CLAUDE.md, Agents).
  - Slots held with no running tests: a dead run. Its lock goes stale when its owner process ends.
- To reach a running agent thread, send with `t3_thread_send` mode `steer`. Put "do this after your current step" in the text for follow-up work. Use `auto` only for idle or finished threads. Reason: a queued message waits for the end of a long turn, and T3 often drops it.

## Brief the agents

- One agent for each area, with a queue of its features. Never start two agents on features that touch the same files. Reason: parallel edits of one file end in merge conflicts in the queue.
- Point every brief that ends in a PR to `hot-cargo-pr-flow`.
- Give each agent that adds a spec section its own section number and requirement number block. Take the blocks above the highest numbers on `main` and in the open PRs (`gh pr diff <n>`). Reason: parallel branches pick the same next number, and `scripts/check-spec-numbers.sh` finds the clash only in the merge queue.
- Before you brief a new player-facing mechanic from a brainstorm pick, write 2 or 3 lines of what the player sees and does, and wait for the player's yes. Tuning, fixes and features that the player described in their own words need no extra check. Reason: a one-line brainstorm option is not a design approval, and a surprise feature once cost a build, a removal and a playtest.
- Risky actions and gambles happen in the game world (a button on a part, an item, a ship), with a visible result and a message that names the outcome. Dialogs only offer clear deals. Reason: the player found dialog-only choices unclear and boring.
- Tests in a brief: a unit test for logic, rolls and math; a game test only for critical, complex or bug-prone areas, with a reason in the PR (`tests/TIERS.md`). Reason: a large sim suite made CI slow and flaky.

## Stability first

- Before you call a build playable or ask for a playtest, the CI `full-suite` check on that commit must be green. It holds the invariant checks (spec 44) and the no-cheat `core-playthrough`. Reason: once about 100 green tests passed and the game was still not playable.
- When the player reports bugs, freeze features and merge only fixes until the bug list is clear. Reason: bugs piled up while about ten feature agents ran.
- When the player reports a bug mid-session, read the live game log and telemetry first (`hot-cargo-telemetry`).
- Check that the full suite runs on `main`: `gh run list --workflow check.yml --branch main --event workflow_dispatch --limit 5` (also `--event schedule`). Reason: GitHub once skipped the scheduled run, and a break stayed hidden.

## Merges and failures

- Agents merge their own PRs (`hot-cargo-pr-flow`). The GitHub merge queue of `main` orders the merges. No agent runs a merge queue of its own.
- A flaky or infrastructure failure in a test that the PR does not touch counts as passing, after a check of the same test on `main` (`hot-cargo-pr-flow`). Reason: slow, flaky gates blocked merges for hours, and one PR merged with 4 real regressions that its owner called unrelated without that check.
- Admin merge: when the only failure is a known flake unrelated to the PR and every other check passed, cancel the pending rerun and merge with `gh pr merge <n> --admin --merge`. Only the coordinator does this; never tell an agent to. Reason: agents act through the owner's account, and each admin merge needs a judgment call.
- After admin merges, cancel the `merge_group` runs whose `gh-readonly-queue/...` branch no longer exists. Reason: each direct merge moves `main`, the queue rebuilds its groups, and the old group runs keep runners busy.
- When CI jams (the free plan runs about 20 jobs at once), cancel other PRs' runs to let the key PR through. Tell their agents not to rerun, then rerun them yourself.
- Never post a commit status by hand. Reason: the owner rejected it; checks come only from CI.
- Note each flake you accept. `../.state/flaky.log` lists the local ones.
- `scripts/integrate.sh` is the old batch merge. It pushes to `main` directly, which the merge queue flow does not use. Read its header before you run it.
- Releases: `hot-cargo-release`.

## The player hold

- `scripts/player-hold.sh on` when the player plays or says the PC is theirs. `off` only when the player says the PC is free. `status` shows it. Reason: an agent once stopped the player's co-op session and used the slot; a "game closed" check is only a snapshot and can be wrong.
- While the hold is on, the scripts refuse slot main and windowed test slots. Headless test slots stay allowed.
- Never brief an agent to do main-slot work. Do it yourself in a free window.

## Work in the player's game slot

Only the coordinator does this, and only while the hold is off:

- Vaults in ship designs: `hot-cargo-player-ships`.
- A new loader version, which needs every game on the PC closed once: `hot-cargo-game-update`.
- Installs need no hold check: `hot-cargo-install`. Keep the newest `main` installed: run `scripts/install-player.sh` after merges. Reason: the player plays the newest build without asking for it. Limit: PC calls work only while your turn is active, so `keep-installed.sh` stops working when the turn ends.
