#!/usr/bin/env bash
# Removes the worktrees whose branch is merged into origin/main, and deletes their local branch. A worktree stays when it is
# the main worktree, detached (play, bisect and base checkouts), has changes or untracked files, is the working folder of a
# running process, or has no commit of its own yet (its tip is on main's first-parent line: a fresh agent worktree).
# Usage: prune-worktrees.sh [--dry-run]   (from any worktree). Prints each worktree it removes or would remove.
# Called by: the coordinator (hot-cargo-coordinate), by hand or after a batch of merges.
set -euo pipefail
cd "$(dirname "$0")/../.."
dry="${1:-}"
git --git-dir .bare fetch -q origin main
# Working folders of running processes. Only folders are read, never command lines.
# main's own commits (the merge commits of the queue). A merged branch's tip sits behind them, a fresh branch's tip on them.
first_parent="$(git --git-dir .bare rev-list --first-parent origin/main)"
busy="$(lsof -d cwd -Fn 2>/dev/null | sed -n 's/^n//p' | sort -u)"
removed=0
while IFS= read -r line; do
  case "$line" in
    "worktree "*) path="${line#worktree }" branch="" ;;
    "branch refs/heads/"*) branch="${line#branch refs/heads/}" ;;
    "") # The end of one worktree's lines.
      [[ -n "${path:-}" && -n "$branch" && "$branch" != main ]] || { path=""; continue; }
      tip="$(git --git-dir .bare rev-parse "refs/heads/$branch")"
      if git --git-dir .bare merge-base --is-ancestor "$tip" origin/main && ! grep -qx "$tip" <<<"$first_parent" \
        && [[ -z "$(git -C "$path" status --porcelain 2>/dev/null)" ]] \
        && ! grep -qxF "$path" <<<"$busy" && ! grep -qF "$path/" <<<"$busy"; then
        echo "remove $(basename "$path") ($branch)"
        if [[ "$dry" != --dry-run ]]; then git --git-dir .bare worktree remove "$path" && git --git-dir .bare branch -q -d "$branch"; fi
        removed=$((removed + 1))
      fi
      path="" ;;
  esac
done < <(git --git-dir .bare worktree list --porcelain; echo)
echo "prune-worktrees: $removed worktrees $([[ "$dry" == --dry-run ]] && echo "would go" || echo removed)"
