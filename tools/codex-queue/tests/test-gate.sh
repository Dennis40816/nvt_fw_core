#!/usr/bin/env bash
# Copyright (c) 2026 Dennis Liu. All rights reserved.
# Offline integration regression: only a disposable repo and disposable queues.
set -euo pipefail
BIN=$(cd "$(dirname "$0")/.." && pwd)
export PYTHONDONTWRITEBYTECODE=1
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0
export GIT_AUTHOR_NAME=GateTest GIT_AUTHOR_EMAIL=fixture
export GIT_COMMITTER_NAME=$GIT_AUTHOR_NAME GIT_COMMITTER_EMAIL=$GIT_AUTHOR_EMAIL
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_CONFIG_COUNT
REAL_GIT=$(command -v git); REAL_PYTHON=$(command -v python)
REAL_SLEEP=$(command -v sleep); REAL_TAR=$(command -v tar)
REAL_PWSH=$(command -v pwsh)
export REAL_GIT REAL_PYTHON REAL_SLEEP REAL_TAR
T=$(mktemp -d "$BIN/tests/.qgate.XXXXXXXX")
T=$(cd "$T" && pwd)
export FAKE_ROOT=$T
passed=0
trap 'echo "FAIL at line $LINENO; synthetic fixtures retained: $T" >&2' ERR
cleanup() {
  cd "$BIN"
  # Resolve and constrain the target before recursive deletion; never delete a repo supplied by a caller.
  "$REAL_PYTHON" - "$T" "$(dirname "$T")" <<'PY'
import os, shutil, stat, sys
from pathlib import Path
p, parent = map(lambda s: Path(s).resolve(), sys.argv[1:])
assert p.parent == parent and p.name.startswith(("qgate.", ".qgate.")) and (p / "fixture.marker").is_file()
def remove_readonly(func, path, exc):
    assert Path(path).resolve().is_relative_to(p)
    if not isinstance(exc[1], PermissionError):
        raise exc[1]
    os.chmod(path, stat.S_IWRITE | stat.S_IREAD)
    func(path)
shutil.rmtree(p, onerror=remove_readonly)
PY
}
touch "$T/fixture.marker"
mkdir -p "$T/mock" "$T/wt" "$T/tmp"
"$REAL_GIT" init -q -b main "$T/repo"
printf 'baseline\n' > "$T/repo/tracked.txt"
printf '{"version": 1}\n' > "$T/repo/packages.lock.json"
"$REAL_GIT" -C "$T/repo" add tracked.txt packages.lock.json
"$REAL_GIT" -C "$T/repo" commit -q -m fixture
cp "$T/repo/.git/config" "$T/initial-config"

cat > "$T/mock/git" <<'SH'
#!/usr/bin/env bash
case " $* " in *" fetch "*) exit 0;; *" config "*|*" push "*) exit 91;; esac
if [ "${1:-}" = commit ] && [ -e "${CASE_DIR:-}/fail-commit" ]; then exit 94; fi
if [ "${1:-}" = worktree ] && [ "${2:-}" = add ] && [ -e "${CASE_DIR:-}/fail-add" ]; then
  mkdir -p "$6"; echo preserve > "$6/partial.txt"; exit 95
fi
if [ "$1" = worktree ] && { [ "$2" = add ] || [ "$2" = remove ]; }; then
  if ! mkdir "$FAKE_ROOT/metadata-active" 2>/dev/null; then touch "$FAKE_ROOT/overlap"; exit 92; fi
  trap 'rmdir "$FAKE_ROOT/metadata-active"' EXIT
  printf '%s\n' "$2" >> "$FAKE_ROOT/metadata.calls"
  if [ "$2" = remove ]; then
    printf '%s\n' "$*" >> "$CASE_DIR/remove.calls"
    [ ! -e "$CASE_DIR/fail-remove" ] || { echo 'synthetic removal failure' >&2; exit 96; }
    if [ -e "$CASE_DIR/check-plain-remove" ]; then
      if "$REAL_GIT" worktree remove "${@: -1}" > "$CASE_DIR/plain-remove.log" 2>&1; then exit 97; fi
    fi
  fi
  "$REAL_SLEEP" 0.15
  "$REAL_GIT" "$@"
  exit $?
fi
exec "$REAL_GIT" "$@"
SH
cat > "$T/mock/df" <<'SH'
#!/usr/bin/env bash
v=$(cat "$CASE_DIR/free")
[ "$v" != unknown ] || exit 1
printf 'Filesystem 1024-blocks Used Available Capacity Mounted\nfake 9999999999 0 %s 0%% /\n' "$v"
SH
cat > "$T/mock/sleep" <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$1" >> "$CASE_DIR/sleep.calls"
if [ "$1" = 120 ]; then
  if [ -e "$CASE_DIR/quota-retry" ] && [ ! -e "$CASE_DIR/quota-retried" ]; then
    touch "$CASE_DIR/quota-retried"
    printf '%s\n' '{"used_percent":5,"sample_age_min":0,"status":"OK"}' > "$CASE_DIR/quota-output"
    exit 0
  fi
  touch "$FAKE_QUEUE/stop"
  touch "$CASE_DIR/stopped-$(basename "$FAKE_QUEUE")"
else
  "$REAL_SLEEP" 0.05
fi
SH
cat > "$T/mock/pwsh" <<'SH'
#!/usr/bin/env bash
if [[ " $* " == *'codex-quota-check.ps1'* ]]; then
  echo check >> "$CASE_DIR/quota.calls"
  [ ! -e "$CASE_DIR/quota-fail" ] || exit 17
  cat "$CASE_DIR/quota-output"
  exit 0
fi
while [ "$#" -gt 0 ]; do
  case "$1" in
    -Out) out=$(cygpath -u "$2"); shift 2;;
    -Log) log=$(cygpath -u "$2"); shift 2;;
    -Dir) dir=$(cygpath -u "$2"); shift 2;;
    -AddDirs) printf '%s\n' "$2" > "$CASE_DIR/add-dirs"; shift 2;;
    *) shift;;
  esac
done
printf '%s\n' "$(basename "$out")" >> "$CASE_DIR/codex.calls"
touch "$CASE_DIR/started-$(basename "$FAKE_QUEUE")"
if [ -e "$CASE_DIR/hold" ]; then
  for ((j=0;j<1000;j++)); do
    [ ! -e "$CASE_DIR/release" ] || break
    "$REAL_SLEEP" 0.02
  done
fi
printf 'synthetic codex output\n' > "$out"
[ ! -f "$CASE_DIR/final-output" ] || cat "$CASE_DIR/final-output" >> "$out"
[ ! -e "$CASE_DIR/result-race" ] || echo preserve-old-result > "$FAKE_QUEUE/done/$(basename "$out" -out.md).md"
printf 'exit=%s\n' "$(cat "$CASE_DIR/code")" > "$log"
if [ -e "$CASE_DIR/quota-exhaust-after-task" ]; then
  printf '%s\n' '{"used_percent":100,"sample_age_min":0,"status":"EXHAUSTED"}' > "$CASE_DIR/quota-output"
fi
if [ ! -e "$CASE_DIR/no-edit" ]; then
  edit=tracked.txt; [ ! -f "$CASE_DIR/edit-path" ] || edit=$(cat "$CASE_DIR/edit-path")
  mkdir -p "$(dirname "$dir/$edit")"
  printf 'codex edit\n' >> "$dir/$edit"
fi
if [ -e "$CASE_DIR/populate-submodule" ]; then
  mkdir -p "$dir/example"
  cp -a "$FAKE_ROOT/submodule-fixture/." "$dir/example/"
fi
SH
cat > "$T/mock/python" <<'SH'
#!/usr/bin/env bash
args=("$@")
if [ "${1:-}" = -I ] && [ "${2:-}" = -B ]; then shift 2; fi
if [[ "$1" == */q.py && ( "${2:-}" == run-accept || "${2:-}" == run-prebuild ) ]]; then
  exec "$REAL_PYTHON" -I -B "$FAKE_ROOT/mock-accept.py" "$@"
fi
exec "$REAL_PYTHON" "${args[@]}"
SH
cat > "$T/mock/tar" <<'SH'
#!/usr/bin/env bash
[ ! -e "$CASE_DIR/fail-evidence" ] || exit 93
exec "$REAL_TAR" "$@"
SH
cat > "$T/mock/dotnet" <<'SH'
#!/usr/bin/env bash
# Legacy-shell tests must never reach an installed SDK or a network restore.
printf '%s\n' "$*" >> "$CASE_DIR/accept.calls"
if [ "${1:-}" = test ]; then
  cat "$CASE_DIR/test-output"
  exit "$(cat "$CASE_DIR/test-exit")"
fi
printf '%s\n' "$*" >> "$CASE_DIR/prebuild.calls"
[ ! -e "$CASE_DIR/prebuild-fail" ] || exit 19
exit 0
SH
cat > "$T/mock-accept.py" <<'PY'
# Exercise the actual validator, argv execution, return codes and evidence parser.
# Only the external dotnet process is replaced; no installed SDK/network is needed.
import os, runpy, subprocess, sys
from pathlib import Path
from unittest.mock import patch
case = Path(os.environ["FAKE_CASE"])
real_run = subprocess.run
def fake_run(args, **kwargs):
    assert kwargs.get("shell") is False
    assert not any(k.upper() in {"GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "OPENAI_API_KEY",
                               "ANTHROPIC_API_KEY", "SSH_AUTH_SOCK"} for k in kwargs["env"])
    if (case / "accept-dirty").exists():
        Path("packages.lock.json").write_bytes(b"accept changed lock\n")
    if (case / "submodule-change").exists():
        path, mode = (case / "submodule-change").read_text().split()
        module = Path(path)
        if mode in ("tracked", "untracked"):
            target = "tracked.txt" if mode == "tracked" else "new.txt"
            (module / target).write_bytes(b"submodule evidence\n")
        elif mode == "head":
            sha = (case / "other-head").read_text().strip()
            real_run(["git", "-C", str(module), "checkout", "-q", "--detach", sha], check=True)
        elif mode == "uninitialized":
            # Gitlinks remain recorded; the missing checkout cannot prove a matching HEAD.
            (module / ".git").rename(module / ".git-uninitialized")
            (case / "submodule-change").unlink()
    if args[0] not in ("dotnet", "pwsh"):
        return real_run(args, **kwargs)
    with (case / "accept.calls").open("a", encoding="utf-8") as out:
        out.write(repr(args) + "\n")
    if sys.argv[1] == "run-prebuild":
        with (case / "prebuild.calls").open("a", encoding="utf-8") as out:
            out.write(repr(args) + "\n")
        return subprocess.CompletedProcess(args, 19 if (case / "prebuild-fail").exists() else 0)
    if args[:2] == ["dotnet", "test"]:
        return subprocess.CompletedProcess(args, int((case / "test-exit").read_text()),
                                           stdout=(case / "test-output").read_bytes())
    return subprocess.CompletedProcess(args, 0, stdout=b"")
sys.argv = sys.argv[1:]
with patch("subprocess.run", fake_run):
    module = runpy.run_path(sys.argv[0])
    module["main"]()
PY
chmod +x "$T/mock/"*
export PATH="$T/mock:$PATH"
GOOD='$ dotnet test tests/Fake.Tests.csproj --no-restore --no-build --nologo
$ git diff --check'
queue() {
  Q="$C/queues/$1"
  mkdir -p "$Q"/{proposed,ready,running,done,failed,work}
  printf '%s\n' "QUOTA_CHECK='pwsh -NoProfile -File codex-quota-check.ps1'" > "$Q/queue.env"
  printf "REPO='%s'\nWT='%s'\nBASE=main\nTEST_TEMP='%s'\nPYDEPS=''\n" "$T/repo" "$T/wt" "$T/tmp" >> "$Q/queue.env"
}
new_case() {
  C="$T/$1"; mkdir -p "$C"
  export CASE_DIR=$C FAKE_CASE
  FAKE_CASE=$(cygpath -m "$C")
  echo $((1000*1048576)) > "$C/free"
  echo 0 > "$C/code"; echo 0 > "$C/test-exit"
  printf '%s\n' '{"used_percent":5,"sample_age_min":0,"status":"OK"}' > "$C/quota-output"
  echo 'Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 1 ms' > "$C/test-output"
  queue q1
}
brief() {
  local name=$1 risk=$2 accept=$3 base=${4:-main}
  {
    echo "Title: $name"
    [ "$risk" = MISSING ] || printf 'Risk: %s\n' "$risk"
    printf 'Base: %s\nWait: none\nGoal: synthetic fixture\nScope: tracked.txt\n' "$base"
    [ "$accept" = MISSING ] || printf 'Accept:\n%s\n' "$accept"
    echo 'Not in scope: everything else'
  } > "$Q/ready/$name.md"
}
run_worker() {
  FAKE_QUEUE=$Q timeout 90 bash "$BIN/qworker.sh" fixture "$Q" > "$Q/work/worker-output.log" 2>&1
}
expect() { "$@" || { echo "assertion failed: $*" >&2; return 1; }; }
report() { passed=$((passed+1)); printf 'PASS %s\n' "$1"; }
removed() {
  local path="$T/wt/q-$(printf %s "$1" | md5sum | cut -c1-6)"
  expect test ! -e "$path"
  expect test -s "$Q/work/$1-head.txt"
  expect "$REAL_GIT" -C "$T/repo" show-ref --verify --quiet "refs/heads/feature/queue/$1"
}

# Reuse only the disposable fixture and mocks in the P0 regression suite.
if [[ "${BASH_SOURCE[0]}" != "$0" ]]; then return; fi

new_case missing-quota-parameter
sed -i '/^QUOTA_CHECK=/d' "$Q/queue.env"
if run_worker; then echo 'missing QUOTA_CHECK must fail' >&2; exit 1; fi
expect grep -q 'must set QUOTA_CHECK' "$Q/work/worker-output.log"
expect test ! -e "$C/quota.calls"
report 'missing required QUOTA_CHECK fails clearly before queue work'


new_case required
brief missing-risk MISSING "$GOOD"
brief empty-risk '   ' "$GOOD"
brief missing-accept R0 MISSING
brief empty-accept R1 '   '
brief prose-accept R0 'Tests should pass.'
brief blank-command R0 "$GOOD"$'\n$'
brief high-risk R3 "$GOOD"
brief duplicate-risk R0 "$GOOD"
echo 'Risk: R1' >> "$Q/ready/duplicate-risk.md"
run_worker

for n in missing-risk empty-risk missing-accept empty-accept prose-accept blank-command high-risk duplicate-risk; do
  expect test -f "$Q/failed/$n.md"
  expect grep -q '## Result (failed)' "$Q/failed/$n.md"
done
expect grep -q 'Risk is missing' "$Q/failed/missing-risk.md"
expect grep -q 'Accept is missing' "$Q/failed/missing-accept.md"
expect test ! -e "$C/codex.calls"
report '1 missing/empty Risk or Accept, duplicate fields and R3 fail before Codex'

new_case templates
echo ACCEPT_POLICY=enforce >> "$Q/queue.env"
i=0
for cmd in \
  'echo unsafe' \
  'python -c "print(1)"' \
  'pwsh -NoProfile -Command "echo unsafe"' \
  'git diff --check; touch PWNED' \
  'git diff --check && touch PWNED' \
  'git diff --check > PWNED' \
  'dotnet test tests/Fake.Tests.csproj --no-restore --filter "$(touch PWNED)"' \
  'dotnet test tests/Fake.Tests.csproj --no-restore -p:CustomAfterMicrosoftCommonTargets=evil.targets' \
  'dotnet test ../Fake.Tests.csproj --no-restore' \
  'dotnet test tests/Fake.Tests.csproj'; do
  i=$((i+1)); brief "unsafe-$i" R0 "$GOOD"$'\n$ '"$cmd"
done
run_worker
for ((j=1;j<=i;j++)); do expect test -f "$Q/failed/unsafe-$j.md"; done
expect test ! -e "$C/codex.calls"
expect test ! -e "$C/accept.calls"
expect test ! -e "$T/repo/PWNED"
report '2 untrusted commands, shell injection and unsafe options rejected'

new_case documents
brief no-tests R0 '$ git diff --check'
run_worker
expect test -f "$Q/done/no-tests.md"
report '2b document diff checks do not require test cases'

new_case passing
brief pass R1 "$GOOD"
run_worker
expect test -f "$Q/done/pass.md"
expect grep -q 'tests=2' "$Q/work/pass-accept.txt"
removed pass
report '3a trusted argv, positive test evidence and success cleanup'

for mode in zero absent skipped nonzero; do
  new_case "$mode"
  case "$mode" in
    zero) echo 'Passed! - Failed: 0, Passed: 0, Skipped: 0, Total: 0, Duration: 1 ms' > "$C/test-output";;
    absent) echo 'Build succeeded.' > "$C/test-output";;
    skipped) echo 'Passed! - Failed: 0, Passed: 0, Skipped: 2, Total: 2, Duration: 1 ms' > "$C/test-output";;
    nonzero) echo 1 > "$C/test-exit";;
  esac
  brief "evidence-$mode" R0 "$GOOD"; run_worker
  expect test -f "$Q/failed/evidence-$mode.md"
  removed "evidence-$mode"
done
report '3b zero/missing/all-skipped evidence and nonzero exit fail'
"$REAL_PYTHON" - "$BIN/q.py" <<'PY'
import runpy, sys
q = runpy.run_path(sys.argv[1])
assert q["test_count"]("Test Run Successful.\nTotal tests: 3\n     Passed: 3\n") == 3
assert q["test_count"]("Test Run Successful.\nTotal tests: 0\n     Passed: 0\n") == 0
assert q["test_count"]("Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2\nTotal tests: 0\n") == 0
args = q["accept_argv"]('dotnet test tests/Fake.Tests.csproj --no-restore --filter "Name=has space" -- RunConfiguration.TreatNoTestsAsError=true')
assert args[5] == "Name=has space"
try:
    q["accept_argv"]('dotnet test tests/Fake.Tests.csproj --no-restore --filter "FullyQualifiedName~A|FullyQualifiedName~B"')
except ValueError:
    pass
else:
    raise AssertionError("r4 forbids quoted pipe characters too")
PY
report '3c detailed/mixed VSTest output and quoted filter argv'

for state in failed running ready missing done-no-branch; do
  new_case "chain-$state"
  parent="parent-$state"
  if [ "$state" != missing ] && [ "$state" != done-no-branch ]; then
    "$REAL_GIT" -C "$T/repo" branch "feature/queue/$parent" main
  fi
  case "$state" in
    failed|running) touch "$Q/$state/$parent.md";;
    ready) brief "$parent" R0 "$GOOD" feature/queue/not-finished;;
    done-no-branch) touch "$Q/done/$parent.md";;
  esac
  brief "child-$state" R0 "$GOOD" "feature/queue/$parent"
  run_worker
  expect test -f "$Q/ready/child-$state.md"
  expect test ! -e "$C/codex.calls"
done
new_case chain-done
"$REAL_GIT" -C "$T/repo" branch feature/queue/parent-done main
sha=$("$REAL_GIT" -C "$T/repo" rev-parse main)
printf '## Result (done)\nBranch: feature/queue/parent-done; head: %s;\n' "$sha" > "$Q/done/parent-done.md"
echo "$sha" > "$Q/work/parent-done-head.txt"
brief child-done R0 "$GOOD" refs/heads/feature/queue/parent-done
run_worker
expect test -f "$Q/done/child-done.md"
new_case chain-order
brief child-order R0 "$GOOD" feature/queue/parent-order
brief parent-order R0 "$GOOD"
# Deterministic oldest-first: blocked child must not starve its ready parent.
touch -t 202001010000 "$Q/ready/child-order.md"
run_worker
expect test -f "$Q/done/parent-order.md"
expect test -f "$Q/done/child-order.md"
expect test "$(head -1 "$C/codex.calls")" = parent-order-out.md
report '4 strict chains block failed/running/ready/missing predecessors; done releases in order'

new_case setup-failure
echo "SETUP='echo setup-change >> tracked.txt; echo untracked-evidence > new.txt; false'" >> "$Q/queue.env"
brief setup-failure R0 "$GOOD"; run_worker
expect test -f "$Q/failed/setup-failure.md"
expect grep -q 'setup failed' "$Q/failed/setup-failure.md"
expect test ! -e "$C/codex.calls"
retained="$T/wt/q-$(printf setup-failure | md5sum | cut -c1-6)"
expect grep -q setup-change "$retained/tracked.txt"
expect grep -q untracked-evidence "$retained/new.txt"
expect grep -q setup-change "$Q/work/setup-failure.patch"
new_case codex-failure
echo 7 > "$C/code"
brief codex-failure R0 "$GOOD"; run_worker
expect test -f "$Q/failed/codex-failure.md"
expect test -f "$Q/work/codex-failure-status.txt" # A committed failure may be clean.
expect test "$("$REAL_GIT" -C "$T/repo" show feature/queue/codex-failure:tracked.txt | tail -1)" = 'codex edit'
removed codex-failure
new_case evidence-failure
touch "$C/fail-evidence"
brief evidence-failure R0 "$GOOD"; echo 7 > "$C/code"; run_worker
expect grep -q 'worktree retained' "$Q/failed/evidence-failure.md"
retained="$T/wt/q-$(printf evidence-failure | md5sum | cut -c1-6)"
expect test -d "$retained"
report '5 failed SETUP preserves dirty trees/evidence; failed evidence keeps trees; clean failures removed'

for mode in floor running configured sibling unknown invalid boundary; do
  new_case "disk-$mode"
  case "$mode" in
    floor) echo $((214*1048576)) > "$C/free";;
    running) touch "$Q/running/existing.md"; echo $((239*1048576)) > "$C/free";;
    configured) echo TASK_RESERVE_GB=40 >> "$Q/queue.env"; echo $((239*1048576)) > "$C/free";;
    sibling)
      first=$Q; queue q2; echo TASK_RESERVE_GB=40 >> "$Q/queue.env"; touch "$Q/running/existing.md"
      Q=$first; echo $((259*1048576)) > "$C/free";;
    unknown) echo unknown > "$C/free";;
    invalid) echo TASK_RESERVE_GB=bad >> "$Q/queue.env";;
    boundary) echo $((220*1048576)) > "$C/free";;
  esac
  brief "disk-$mode" R0 "$GOOD"; run_worker
  if [ "$mode" = boundary ]; then expect test -f "$Q/done/disk-$mode.md"
  else expect test -f "$Q/ready/disk-$mode.md"; expect test ! -e "$C/codex.calls"; fi
done
report '6a 215 floor, running/sibling/configurable reserve, exact boundary and unknown fail-closed'

wait_marker() {
  local j
  for ((j=0;j<1500;j++)); do
    if compgen -G "$C/started-*" >/dev/null && compgen -G "$C/stopped-*" >/dev/null; then return; fi
    "$REAL_SLEEP" 0.02
  done
  echo 'concurrent reservation test timed out' >&2; return 1
}
new_case disk-race
echo $((230*1048576)) > "$C/free"; touch "$C/hold"
brief reserve-a R0 "$GOOD"; q1=$Q
queue q2; brief reserve-b R0 "$GOOD"; q2=$Q
Q=$q1; run_worker & p1=$!
Q=$q2; run_worker & p2=$!
wait_marker
touch "$C/release"
wait "$p1"; wait "$p2"
expect test "$(wc -l < "$C/codex.calls")" -eq 1
expect test "$(find "$C/queues" -path '*/ready/*.md' | wc -l)" -eq 1
expect test "$(find "$C/queues" -path '*/done/*.md' | wc -l)" -eq 1
report '6b concurrent cross-queue claims cannot spend the same disk reserve'

new_case locks
brief lock-a R0 "$GOOD"; q1=$Q
queue q2; brief lock-b R0 "$GOOD"; q2=$Q
Q=$q1; run_worker & p1=$!
Q=$q2; run_worker & p2=$!
wait "$p1"; wait "$p2"
expect test -f "$q1/done/lock-a.md"
expect test -f "$q2/done/lock-b.md"
expect test ! -e "$T/overlap"
expect test ! -e "$T/repo/.git/qworker-worktree.lock"
expect test ! -e "$C/queues/.qworker-claim.lock"
# Main tree plus the deliberately retained dirty SETUP and evidence-failure trees.
expect test "$("$REAL_GIT" -C "$T/repo" worktree list --porcelain | grep -c '^worktree ')" -eq 3
expect cmp -s "$T/initial-config" "$T/repo/.git/config"
report '7 concurrent worktree add/remove serialized; locks released; git config unchanged'

cleanup
printf 'ALL PASS: %s groups; offline synthetic repo/queues only; fixtures cleaned\n' "$passed"
