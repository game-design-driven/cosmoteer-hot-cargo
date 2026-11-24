---
name: hot-cargo-pr-flow
description: Take a Hot Cargo change from its branch to a merged PR and an installed build. Owner merge with auto-merge, required checks, tests outside CI, failures that look unrelated, PR text. Use when you open, update or merge a Hot Cargo PR, and point every agent brief that ends in a PR to it.
---

# From branch to merged PR

The rules are in CLAUDE.md (Git, Spec, Agents) and CONTRIBUTING.md (Git). This skill gives the order.

1. Branch from `origin/main` in your own worktree: `git --git-dir ../.bare worktree add ../<name> -b <type>/<name> origin/main`.
2. In the same change, make the spec lines true that the change touches. For a visible change, take screenshots (`hot-cargo-screenshots`). Before you pick a new spec section or requirement number, check the open PRs (`gh pr diff <n>`) for numbers in use.
3. Commit with conventional commits (`feat:`, `fix:`, `docs:`, `chore:`). Never credit an AI, a model or a tool.
4. Before each push, run the game tests that the change needs (`scripts/autotest-sim.sh <names>`, `scripts/autotest-affected.sh`). PR runs run no game tests.
   - `autotest-affected.sh` runs only the smoke tier. Before a push that changes shared data (presets, enum lists, default rows), run every test of the touched areas: `AUTOTEST_TIER=full scripts/autotest-affected.sh`. Reason: the queue runs only the PR's set, so such a break shows first in the full suite on `main`.
5. Push and open the PR. Start the PR text with 🤖 and keep it short. Add images of visible changes (`gh-pr-images` skill). The merge queue runs the PR's game test set: by default the affected tests and the baseline. Change it in the `### Game tests` block of the PR text (`+ <test>`, `- <test>`, `all`, `none: <reason>`; CONTRIBUTING.md). The PR run's summary shows the set; an unknown test fails it. Usually fewer tests are better. A change to world generation or random rolls takes `all`. Reason: such a change once passed its own set and broke 6 other tests.
   - `### Game tests` is the last block of the PR text. `scripts/ci-test-set.sh` reads every line after it as a test line, so a checkbox or an image there fails `build`. `gh pr edit --attach` adds images at the end: move them above the block (`gh pr view <n> --json body`, edit, `gh pr edit <n> --body-file <file>`).
6. Tests outside CI: run `LIST_ONLY=1 RUNNER=pc scripts/autotest-affected.sh`. Run only the listed tests that the merge depends on, on the PC (`hot-cargo-testing`). Tick the box of the PR template.
7. Mark the PR ready (`gh pr ready`): a draft PR does not merge. Then queue it: `scripts/queue-pr.sh <n>`. It turns on auto-merge, adds the queue entry itself when the checks passed and `gh pr merge --auto` queued nothing (this happens), prints the queue position, and fails loudly on any other state. While checks still run, run it again after they pass if no entry appears. Do not use `gh pr merge --merge`: the merge queue sets the merge method, and with `--merge` the command only prints a warning and queues nothing. Never use `--admin` (only the coordinator does, `hot-cargo-coordinate`). When the checks pass, the PR goes into the merge queue of `main`. The queue tests it on top of the newest `main` and the PRs ahead of it, then merges it. Watch the PR. Do not poll it.
8. The PR does not have to be up to date with `main`. When `main` moves, read what the new commits intend (`git log`, `gh pr view`). Only if they conflict with the change or change what it does, merge `origin/main` into the branch, adapt, and push.
9. If a required check fails, read it: `gh pr checks`, `gh run view <run id> --log-failed`, and the job summary of the `game-tests` shard. A failure in the merge queue removes the PR from the queue. Then follow the next section.
10. After the merge, install the build into the player's game: `scripts/install-player.sh` (`hot-cargo-install`).

## A failure that looks unrelated

1. Check the same test on `main` first: the full suite runs there at most 3 hours after a merge (`gh run list --workflow check --branch main --event workflow_dispatch --limit 5`; also `--event schedule`), and queue runs show other PRs (`--event merge_group`). Read the job summary of a recent run.
2. If the test passes on `main`, the failure belongs to the PR. Fix it. A PR once merged with 4 real regressions that its owner called unrelated.
3. If the test is flaky (it passes on its retry or on a rerun) or the failure is an infrastructure failure, it counts as passing. On the PR's own run, rerun the failed jobs: `gh run rerun <run id> --failed`. In the merge queue, add the PR again: `scripts/queue-pr.sh <n>`. Note it in the PR in one line.
4. If the test also fails on `main`, `main` is broken. Do not merge over it. Tell the coordinator.

Infrastructure failures and the log words of a game failure: `hot-cargo-testing`, "Infrastructure failures".

Do not babysit a fragile test. Decide in one look: a product bug or test variance. Fix a product bug. For test variance, note what the run proved, start at most one adjusted run in the background, and go on with real work. Reason: reruns of a long test cost hours and prove little.

## Merge queue traps

- A queue entry in state UNMERGEABLE conflicts with a PR ahead of it in the queue, not with `main`. Find it with `git merge-tree` against the head of each queued PR.
- While a PR has a queue entry (any state), a push to its branch fails ("protected branch hook declined"). Remove the entry first: `gh api graphql -f query='mutation { dequeuePullRequest(input: {id: "<PR node id>"}) { clientMutationId } }'` (node id: `gh pr view <n> --json id`). Then push, and run `scripts/queue-pr.sh <n>` again.
