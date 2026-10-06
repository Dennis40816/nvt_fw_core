#!/usr/bin/env bash
# Copyright (c) 2026 Dennis Liu. All rights reserved.
# Refill the queue: one read-only Codex run (default model and effort) writes up to 10 briefs from the work board
# into <queue>/proposed/. The commander checks them and moves the good ones to ready/.
# Usage: qrefill.sh <queue>   (<queue>: a queue folder path, or its name beside the tool directory); REF=<ref> reads another board ref.
# Settings come from <queue>/queue.env (TRUNK_WT BASE ...), the prompt from <queue>/refill-prompt.md.
BIN=$(cd "$(dirname "$0")" && pwd)
Q=${1:?queue folder (a path, or its name beside bin/)}
case "$Q" in /*|?:*) ;; *) Q="$BIN/../$Q";; esac
Q=$(cd "$Q" && pwd) && . "$Q/queue.env" || { echo "no readable queue folder or queue.env: $1" >&2; exit 1; }
: "${TRUNK_WT:?queue.env must set TRUNK_WT (read-only refill worktree path)}"
: "${BASE:?queue.env must set BASE (base Git ref)}"
WINQ=$(cygpath -w "$Q"); WINBIN=$(cygpath -w "$BIN")
T=$TRUNK_WT
N=refill-$(date +%m%d-%H%M)
git -C "$T" fetch -q origin "${BASE#origin/}" && git -C "$T" checkout -q --detach "${REF:-$BASE}"
rm -f "$Q/work/$N-out.md" "$Q/work/$N.log"
env -u HOME pwsh -NoProfile -File "$WINBIN\\run-codex-ws.ps1" -PromptFile "$WINQ\\refill-prompt.md" -Out "$WINQ\\work\\$N-out.md" -Log "$WINQ\\work\\$N.log" \
  -Sandbox read-only -Dir "$(cygpath -w "$T")" > /dev/null 2>&1
[ -f "$Q/work/$N-out.md" ] && python -I -B "$BIN/q.py" split "$Q/work/$N-out.md" "$Q/proposed"
echo "refill $N done: $Q/work/$N-out.md"
