#!/usr/bin/env bash
# Copyright (c) 2026 Dennis Liu. All rights reserved.
# One-off focused refill: like qrefill.sh but with <queue>/refill-focus-prompt.md. Usage: qfocus.sh <queue>; REF=<ref>.
BIN=$(cd "$(dirname "$0")" && pwd)
Q=${1:?queue}
case "$Q" in /*|?:*) ;; *) Q="$BIN/../$Q";; esac
Q=$(cd "$Q" && pwd) && . "$Q/queue.env" || exit 1
: "${TRUNK_WT:?queue.env must set TRUNK_WT (read-only refill worktree path)}"
: "${BASE:?queue.env must set BASE (base Git ref)}"
WINQ=$(cygpath -w "$Q"); WINBIN=$(cygpath -w "$BIN"); T=$TRUNK_WT
N=focus-$(date +%m%d-%H%M)
git -C "$T" fetch -q origin "${BASE#origin/}" && git -C "$T" checkout -q --detach "${REF:-$BASE}"
env -u HOME pwsh -NoProfile -File "$WINBIN\\run-codex-ws.ps1" -PromptFile "$WINQ\\refill-focus-prompt.md" -Out "$WINQ\\work\\$N-out.md" -Log "$WINQ\\work\\$N.log" \
  -Sandbox read-only -Dir "$(cygpath -w "$T")" > /dev/null 2>&1
[ -f "$Q/work/$N-out.md" ] && python -I -B "$BIN/q.py" split "$Q/work/$N-out.md" "$Q/proposed"
echo "focus $N done: $Q/work/$N-out.md"
