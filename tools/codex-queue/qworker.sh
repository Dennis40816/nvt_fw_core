#!/usr/bin/env bash
# Copyright (c) 2026 Dennis Liu. All rights reserved.
# Codex queue worker: qworker.sh <worker-name> <queue> (path, or name beside bin/).
# queue.env: REPO WT BASE; trusted SETUP; shared EXTRA_ADD_DIRS are ignored;
# MIN_FREE_GB (default 215), TASK_RESERVE_GB (default 20 GiB per running/pending/new task).
# QUOTA_CHECK (required command unless ALLOW_CREDITS=1), ALLOW_CREDITS (default 0).
# ACCEPT_POLICY=warn (default) or enforce; result checks remain mandatory in both.
# Briefs: see q.py. Keep branches/results; remove only clean, owned worktrees.
# No pushes. Wait 120 s when no eligible task; <queue>/stop stops new claims.
W=${1:?worker name}
BIN=$(cd "$(dirname "$0")" && pwd)
Q=${2:?queue folder (a path, or its name beside bin/)}
case "$Q" in /*|?:*) ;; *) Q="$BIN/../$Q";; esac
unset SETUP EXTRA_ADD_DIRS MIN_FREE_GB TASK_RESERVE_GB QUOTA_CHECK ALLOW_CREDITS ACCEPT_POLICY
Q=$(cd "$Q" && pwd) && . "$Q/queue.env" || { echo "no readable queue folder or queue.env: $2" >&2; exit 1; }
: "${REPO:?queue.env must set REPO (repository path)}"
: "${WT:?queue.env must set WT (worktree parent path)}"
: "${BASE:?queue.env must set BASE (base Git ref)}"
ALLOW_CREDITS=${ALLOW_CREDITS:-0}
if [ "$ALLOW_CREDITS" != 1 ] && [ -z "${QUOTA_CHECK:-}" ]; then
  echo 'queue.env must set QUOTA_CHECK (quota command), or ALLOW_CREDITS=1' >&2; exit 1
fi
ACCEPT_POLICY=${ACCEPT_POLICY:-warn}
case "$ACCEPT_POLICY" in warn|enforce) ;; *) echo 'ACCEPT_POLICY must be warn or enforce' >&2; exit 1;; esac
WINQ=$(cygpath -w "$Q")
WINBIN=$(cygpath -w "$BIN")
export DOTNET_CLI_UI_LANGUAGE=en
export PYTHONDONTWRITEBYTECODE=1 QUEUE_BUILD_NOTES="$Q/build-notes.md"
umask 077
q() { python -I -B "$BIN/q.py" "$@"; }
log() { echo "$(date +%m-%dT%H:%M) [$W] $*" >> "$Q/work/workers.log"; }
stop_queue() { touch "$Q/stop" && log "$1"; }
host_setup() (
  # A subshell removes these only for SETUP, preserving the worker/Codex environment.
  unset GH_TOKEN GITHUB_TOKEN GH_ENTERPRISE_TOKEN OPENAI_API_KEY ANTHROPIC_API_KEY SSH_AUTH_SOCK
  eval "$SETUP"
)
check_quota() {
  [ "$ALLOW_CREDITS" != 1 ] || return 0
  local sample
  sample=$(timeout 60 bash -c "$QUOTA_CHECK" 2>/dev/null) || return 1
  printf '%s\n' "$sample" | q quota 2>/dev/null
}

# Sibling queues share the claim lock; all worktrees of a repo share its metadata lock.
CLAIM_LOCK="$(dirname "$Q")/.qworker-claim.lock"
COMMON_DIR=$(git -C "$REPO" rev-parse --path-format=absolute --git-common-dir) || exit 1
WT_LOCK="$(cygpath -u "$COMMON_DIR")/qworker-worktree.lock"
HELD_LOCK=""
unlock() { [ -z "$HELD_LOCK" ] || rmdir "$HELD_LOCK"; HELD_LOCK=""; }
trap unlock EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
lock() {
  local attempt=0
  while ! mkdir "$1" 2>/dev/null; do
    attempt=$((attempt+1))
    if [ "$attempt" -ge 60 ]; then log "lock unavailable (manual review if stale): $1"; return 1; fi
    sleep 1
  done
  HELD_LOCK=$1
}

worktree_clean() (
  local repo=${1:-$D} expected=${2:-} dirty entry metadata path sha
  if [ -n "$expected" ]; then
    sha=$(git -C "$repo" rev-parse HEAD) || return 1
    [ "$sha" = "$expected" ] || return 1
  fi
  dirty=$(git -C "$repo" status --porcelain --ignore-submodules=none) || return 1
  [ -z "$dirty" ] || return 1
  # Read every gitlink directly: foreach skips uninitialized checkouts, while
  # submodule status also marks populated but inactive checkouts with '-'.
  set -o pipefail
  git -C "$repo" ls-files --stage -z | while IFS= read -r -d '' entry; do
    case "$entry" in
      '160000 '*)
        metadata=${entry%%$'\t'*}; path=${entry#*$'\t'}
        sha=${metadata#160000 }; sha=${sha% *}
        worktree_clean "$repo/$path" "$sha" || exit 1;;
    esac
  done
)

finish() {  # finish <name> <done|failed> <reason>; cleanup never changes the result.
  local evidence=yes cleanup="" state=$2 reason=$3 clean=no wt_locked=no pending=""
  if [ -n "${D:-}" ] && [ -d "$D" ]; then
    cd "$REPO" || return 1
    if lock "$WT_LOCK"; then wt_locked=yes; fi
    HEAD=$(git -C "$D" rev-parse HEAD) || evidence=no
    if worktree_clean >> "$Q/work/$1-git.log" 2>&1; then clean=yes; fi
    if [ "$state" = failed ] || [ "$clean" = no ]; then
      # Preserve setup/commit failures too: tracked diff plus nonignored untracked files.
      (set -o pipefail; cd "$D" && git status --porcelain --ignore-submodules=none > "$Q/work/$1-status.txt" &&
        git submodule status --recursive >> "$Q/work/$1-status.txt" &&
        git submodule foreach --quiet --recursive '
          printf "\nSubmodule: %s; recorded HEAD: %s\n" "$displaypath" "$sha1" &&
            git rev-parse HEAD && git status --porcelain --ignore-submodules=none
        ' >> "$Q/work/$1-status.txt" &&
        git diff --binary HEAD > "$Q/work/$1.patch" &&
        git submodule foreach --quiet --recursive '
          git diff --binary HEAD --src-prefix="a/$displaypath/" --dst-prefix="b/$displaypath/"
        ' >> "$Q/work/$1.patch" &&
        { git ls-files --others --exclude-standard -z &&
          git submodule foreach --quiet --recursive '
            git ls-files --others --exclude-standard -z | while IFS= read -r -d "" path; do
              printf "%s/%s\0" "$displaypath" "$path"
            done
          '; } | tar --null -T - -cf "$Q/work/$1-untracked.tar") >> "$Q/work/$1-git.log" 2>&1 || evidence=no
      cleanup+=" Evidence: work/$1-status.txt, work/$1.patch, work/$1-untracked.tar."
    fi
    # Save the full head before removal; retain the running reservation through cleanup.
    printf '%s\n' "${HEAD:-unknown}" > "$Q/work/$1-head.txt" || evidence=no
    if [ "$evidence" != yes ]; then
      pending='Evidence unavailable; worktree retained for manual recovery.'
    elif [ "$clean" != yes ]; then
      pending='Worktree/submodule dirty or clean check unavailable; worktree retained for manual recovery.'
    elif [ "$wt_locked" != yes ]; then
      pending='Worktree lock unavailable; worktree retained for manual recovery.'
    elif git worktree remove --force "$D" >> "$Q/work/$1-git.log" 2>&1; then
      cleanup+=" Worktree removed."
    else
      pending="Worktree removal failed; manual cleanup required (see work/$1-git.log)."
    fi
    [ "$wt_locked" != yes ] || unlock
  fi
  lock "$CLAIM_LOCK" || return 1
  if [ -e "$Q/done/$1.md" ] || [ -e "$Q/failed/$1.md" ]; then
    log "$1: result destination exists; running brief and evidence retained"
    unlock; return 1
  fi
  if [ -n "$pending" ]; then
    # Native Python must receive a Windows-readable path, rather than an MSYS path.
    printf '%s\t%s\tcleanup pending: %s\n' "$1" "$(cygpath -m "$D")" "$pending" >> "$Q/work/cleanup-pending.log" || { unlock; return 1; }
  fi
  { echo; echo "## Result ($state)"; echo; echo "Worker $W, $(date +%F\ %H:%M). $reason$cleanup"; echo "Branch: ${BR:-none}; head: ${HEAD:-none}; worktree: ${D:-none}"
    [ -z "$pending" ] || printf 'cleanup pending: %s\n' "$pending"
    [ ! -f "$Q/work/$1-out.md" ] || echo 'Codex diagnostic output retained locally; not copied into brief.'
    [ -s "$Q/work/$1-accept.txt" ] && { echo; echo "Accept summary:"; echo; cat "$Q/work/$1-accept.txt"; }
    true
  } >> "$Q/running/$1.md" || { unlock; return 1; }
  mv -n -- "$Q/running/$1.md" "$Q/$state/$1.md"
  [ ! -e "$Q/running/$1.md" ] || { unlock; return 1; }
  unlock
  log "$1 -> $state: $reason$cleanup${pending:+ cleanup pending: $pending}"
}

chain_ready() {
  # Invalid briefs must reach failed even if their Base also has an unmet dependency.
  CLAIM_BASE=""
  q check "$1" >/dev/null 2>&1 || return 0
  CLAIM_BASE=$(cd "$REPO" && q chain-base "$1" "$Q" "$BASE" 2>/dev/null) || {
    log "$(basename "$1" .md): blocked; predecessor needs done and accepted SHA evidence"
    return 1
  }
}

idle=0
while [ ! -e "$Q/stop" ]; do
  # Skip the quota probe (a pwsh start) while there is nothing to claim.
  if ! compgen -G "$Q/ready/*.md" >/dev/null; then
    [ "$idle" = 1 ] || log "ready is empty, waiting (checks every 120 s)"
    idle=1; sleep 120; continue
  fi
  check_quota; quota_rc=$?
  if [ "$quota_rc" = 3 ]; then
    stop_queue 'quota exhausted (fresh sample): queue stopped'; break
  elif [ "$quota_rc" != 0 ]; then
    log 'warning: quota check failed or unreadable; not claiming; retry in 120 s'
    sleep 120; continue
  fi
  lock "$CLAIM_LOCK" || { sleep 120; continue; }
  f=""
  while IFS= read -r candidate; do
    if ! q unique "$Q" "$(basename "$candidate" .md)" "$candidate" >/dev/null 2>&1; then
      log 'ready task has an invalid/duplicate name or retained work; not claiming'
      continue
    fi
    if chain_ready "$candidate"; then f=$candidate; break; fi
  done < <(ls -1tr "$Q"/ready/*.md 2>/dev/null)
  if [ -z "$f" ]; then
    unlock
    [ "$idle" = 1 ] || log "no eligible ready task, waiting (checks every 120 s)"
    idle=1; sleep 120; continue
  fi
  idle=0
  # Counting and claiming are atomic across sibling queues.
  required=$(q required-free "$Q" "${MIN_FREE_GB:-215}" "${TASK_RESERVE_GB:-20}" 2>/dev/null)
  free_kb=$(df -Pk "$WT" 2>/dev/null | awk 'NR==2{print $4}')
  if [[ ! "$required" =~ ^[0-9]+$ || ! "$free_kb" =~ ^[0-9]+$ ]] || [ "$free_kb" -lt "$((required * 1048576))" ]; then
    unlock
    [ "${lowdisk:-0}" = 1 ] || log "disk gate: unknown settings/space or insufficient reserve (need ${required:-unknown} GiB); not claiming"
    lowdisk=1; sleep 120; continue
  fi
  lowdisk=0
  N=$(basename "$f" .md)
  if [ -e "$Q/stop" ] || [ -e "$Q/running/$N.md" ]; then unlock; sleep 120; continue; fi
  mv -n -- "$f" "$Q/running/$N.md" 2>/dev/null
  [ ! -e "$f" ] || { unlock; continue; }
  unlock
  B="$Q/running/$N.md"; BR=""; HEAD=""; D=""
  if ! reason=$(q check "$B" 2>&1); then finish "$N" failed "$reason"; continue; fi
  base=$CLAIM_BASE
  wait_for=$(q get "$B" Wait) && model=$(q get "$B" Model) && effort=$(q get "$B" Effort) || { finish "$N" failed 'brief parsing failed'; continue; }
  case "${wait_for,,}" in ""|none) ;; *) finish "$N" failed "Wait is not none"; continue;; esac
  BR=feature/queue/$N; D=$WT/q-$(printf %s "$N" | md5sum | cut -c1-6)
  log "$N: start (base $base)"
  # Serialize fetch/add/remove. Never delete a preexisting tree or branch on retry.
  if lock "$WT_LOCK"; then
    if [ -e "$D" ] || [ -L "$D" ]; then rc=1; D=""
    else
      (cd "$REPO" && { git fetch -q origin "${BASE#origin/}" 2>/dev/null; git worktree add -q -b "$BR" "$D" "$base"; }) >> "$Q/work/$N-git.log" 2>&1
      rc=$?
      # Even a partially created directory from a failed add is not safe to remove.
      [ "$rc" = 0 ] || D=""
    fi
    unlock
  else rc=1; D=""; fi
  [ "$rc" = 0 ] || { finish "$N" failed "worktree creation failed (see git log)"; continue; }
  cd "$D" || { finish "$N" failed "no worktree"; continue; }
  BASE_SHA=$(git rev-parse HEAD) || { finish "$N" failed 'cannot resolve base SHA'; continue; }
  [ -z "$SETUP" ] || host_setup >> "$Q/work/$N-setup.log" 2>&1 || { finish "$N" failed 'setup failed (see setup log)'; continue; }
  q run-prebuild "$B" "$Q/work/$N" || { finish "$N" failed 'prebuild failed (see prebuild log)'; continue; }
  q prompt "$B" "$(cygpath -m "$D")" "$BR" "$BASE_SHA" > "$Q/work/$N-prompt.md" || { finish "$N" failed "prompt creation failed"; continue; }
  task_tmp="$Q/work/$N-tmp"
  mkdir "$task_tmp" || { finish "$N" failed 'task temporary directory already exists'; continue; }
  extra=(); [ -n "$model" ] && extra+=(-Model "$model"); [ -n "$effort" ] && extra+=(-Effort "$effort")
  env -u HOME TEMP="$(cygpath -w "$task_tmp")" TMP="$(cygpath -w "$task_tmp")" TMPDIR="$(cygpath -w "$task_tmp")" \
    pwsh -NoProfile -File "$WINBIN\\run-codex-ws.ps1" -PromptFile "$WINQ\\work\\$N-prompt.md" -Out "$WINQ\\work\\$N-out.md" -Log "$WINQ\\work\\$N.log" \
    -Sandbox workspace-write -AddDirs "$(cygpath -w "$task_tmp")" -Dir "$(cygpath -w "$D")" "${extra[@]}" > /dev/null 2>&1
  code=$(grep -E '^exit=' "$Q/work/$N.log" | tail -1 | sed 's/exit=\([0-9-]*\).*/\1/')
  if ! reason=$(q check-diff "$B" "$BASE_SHA" 2>&1); then finish "$N" failed "$reason"; continue; fi
  status=$(git status --porcelain) || { finish "$N" failed 'cannot read task status'; continue; }
  if [ -n "$status" ]; then
    git add -A && git commit -q -m "wip(queue/$N): $N (Codex, verified inside the sandbox only)" || { finish "$N" failed "could not preserve task commit"; continue; }
  fi
  HEAD=$(git rev-parse HEAD)
  if [ "$code" != "0" ] || [ ! -f "$Q/work/$N-out.md" ]; then finish "$N" failed "Codex exit=$code, final message present: $([ -f "$Q/work/$N-out.md" ] && echo yes || echo no)"; continue; fi
  if grep -q -i "usage limit" "$Q/work/$N-out.md"; then stop_queue 'usage limit: queue stopped'; finish "$N" failed "usage limit: queue stopped"; break; fi
  if grep -q -i -E "^[*[:space:]]*LANE-STOP" "$Q/work/$N-out.md"; then finish "$N" failed "Codex stopped itself (LANE-STOP)"; continue; fi
  q run-accept "$B" "$Q/work/$N" "$BASE_SHA" > "$Q/work/$N-accept.txt" 2> "$Q/work/$N-accept-error.log"; rc=$?
  if [ "$rc" = 0 ] && [ "$(git rev-parse HEAD)" = "$HEAD" ]; then
    finish "$N" done "Codex finished; Accept passed (policy=$ACCEPT_POLICY; dotnet test requires positive test evidence)"
  else finish "$N" failed "Accept failed, missing test evidence or HEAD changed"; fi
done
log "stopped"
