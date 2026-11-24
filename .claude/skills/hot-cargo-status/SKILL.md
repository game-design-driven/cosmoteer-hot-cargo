---
name: hot-cargo-status
description: See the whole Hot Cargo state in one run - main and its full suite, the merge queue and PRs that left it, open PRs, releases, the versions installed on the Mac and the PC, the player hold, the Mac sim cap and free disk space. Use at the start of a coordination session, before you report a state, and when something looks stuck.
---

# Status

Run `scripts/status.sh` from any worktree. It only reads, and it takes about 30 s (the PC answer is the slowest part).

It prints:
- `main`: the last commit, and the newest full-suite result on `main` (a skipped check does not count).
- The merge queue: each entry with its position and state.
- Each open PR: branch, age, time since its last commit, check state, queue state (draft, queued, auto-merge), and "LEFT the queue" with the time and reason when the queue removed it last.
- The newest dev release and the newest release.
- The Hot Cargo version installed in the Mac app and in the player's game on the PC ("PC offline" when the PC does not answer within 30 s).
- The player hold, `HOT_CARGO_SIM_JOBS` and the free disk space of this Mac.

What to do with it:
- A PR that left the queue on `failed_checks`: read the failure (`hot-cargo-pr-flow`, "A failure that looks unrelated"). A known flake: `scripts/queue-pr.sh <n>`.
- A full suite that failed, or none for hours: `hot-cargo-coordinate`, Stability first.
- An installed version older than the newest dev release: `hot-cargo-install`.
- Low free disk space: see `hot-cargo-testing`, Infrastructure failures.

Report only what the script printed in the same turn. Write "not checked" for a part that did not answer.
