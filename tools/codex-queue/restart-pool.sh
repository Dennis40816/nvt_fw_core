#!/usr/bin/env bash
# Copyright (c) 2026 Dennis Liu. All rights reserved.
# One-off pool restart: stop existing workers, then start replacement workers and release requeued briefs.
BIN=$(cd "$(dirname "$0")" && pwd)
Q=${1:?queue folder (a path, or its name beside the tool directory)}
case "$Q" in /*|?:*) ;; *) Q="$BIN/../$Q";; esac
Q=$(cd "$Q" && pwd) || exit 1
L=$Q/work/workers.log
start=$(wc -l < "$L")
touch "$Q/stop"
for i in $(seq 1 40); do
  n=$(tail -n +$((start+1)) "$L" | grep -c '\] stopped')
  [ "$n" -ge 4 ] && break
  sleep 60
done
rm -f "$Q/stop"
echo "$(date +%m-%dT%H:%M) restart-pool: old workers stopped=$n, starting new" >> "$L"
for w in w5 w6 w7 w8 w9 w10; do nohup bash "$BIN/qworker.sh" "$w" "$Q" > /dev/null 2>&1 & sleep 3; done
mv "$Q"/work/requeue/*.md "$Q/ready/" 2>/dev/null
echo "$(date +%m-%dT%H:%M) restart-pool: new workers started, requeue set released" >> "$L"
