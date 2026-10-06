#!/usr/bin/env bash
# Copyright (c) 2026 Dennis Liu. All rights reserved.
# P0 second-round regressions, using the same offline disposable fixture as test-gate.
set -euo pipefail
source "$(dirname "$0")/test-gate.sh"

"$REAL_PYTHON" -I -B "$BIN/tests/test-p0.py" "$BIN/q.py" "$T"
report 'U1/U2/U3/U5/U6-local/U7/U9/U12/U16/42 parser and boundary unit regressions'

for mode in permitted-domain permitted-ci blocked-domain missing-domain; do
  new_case "risk-config-$mode"
  path=src/Project.Domain/Logic.cs
  case "$mode" in
    permitted-domain) printf 'R2 src/Project.Domain/\n' > "$Q/risk-floor.txt";;
    permitted-ci) path=.github/workflows/ci.yml; printf 'R2 .github\n' > "$Q/risk-floor.txt";;
    blocked-domain) printf 'R3 src/Project.Domain/\n' > "$Q/risk-floor.txt";;
  esac
  echo "$path" > "$C/edit-path"
  brief "risk-config-$mode" R2 '$ git diff --check'
  sed -i "s@Scope: tracked.txt@Scope: $path@" "$Q/ready/risk-config-$mode.md"
  run_worker
  if [[ "$mode" == permitted-* ]]; then
    expect test -f "$Q/done/risk-config-$mode.md"
    expect grep -q 'exit=0' "$Q/work/risk-config-$mode-accept.txt"
    removed "risk-config-$mode"
  else
    expect test -f "$Q/failed/risk-config-$mode.md"
    expect grep -q 'host risk floor' "$Q/failed/risk-config-$mode.md"
    expect test ! -e "$C/accept.calls"
  fi
done
report 'synthetic queue rules permit reviewed R2 paths; explicit and missing-config legacy R3 remain blocked'

for policy in warn enforce; do
  for kind in config annotation whitespace glob; do
    new_case "risk-early-$policy-$kind"
    echo "ACCEPT_POLICY=$policy" >> "$Q/queue.env"
    brief "risk-early-$policy-$kind" R2 '$ git diff --check'
    case "$kind" in
      config) printf '# invalid host policy\nR9 src/\n' > "$Q/risk-floor.txt"; error='risk-floor.txt line 2';;
      annotation) scope='src/A.cs (only if changed)'; error='Scope line 6';;
      whitespace) scope='src/A B.cs'; error='Scope line 6';;
      glob) scope='src/*.cs'; error='Scope line 6';;
    esac
    if [ "$kind" != config ]; then sed -i "s@Scope: tracked.txt@Scope: $scope@" "$Q/ready/risk-early-$policy-$kind.md"; fi
    run_worker
    expect test -f "$Q/failed/risk-early-$policy-$kind.md"
    expect grep -q "$error" "$Q/failed/risk-early-$policy-$kind.md"
    expect test ! -e "$C/codex.calls"
    expect test ! -e "$C/accept.calls"
  done
done
report 'invalid risk-floor and malformed Scope fail before Codex/Accept in warn and enforce'

new_case queue-templates
printf '%s\n' 'dotnet test {project} -c Release --filter {filter}' \
  'pwsh -NoProfile -File ./scripts/verify.ps1 -ValidateOnly' > "$Q/accept-templates.txt"
brief queue-templates R0 '$ dotnet test tests/Fake.Tests.csproj -c Release --filter Name=example
$ pwsh -NoProfile -File ./scripts/verify.ps1 -ValidateOnly
$ git diff --check'
echo 'Prebuild: Desktop' >> "$Q/ready/queue-templates.md"
run_worker
expect test -f "$Q/done/queue-templates.md"
expect test "$(wc -l < "$C/prebuild.calls")" -eq 1
expect grep -q 'template=dotnet-test, exit=0, tests=2' "$Q/work/queue-templates-accept.txt"
report 'r4 queue templates pass claim/check-diff/Prebuild/argv Accept through the actual worker'

new_case queue-template-injection
echo ACCEPT_POLICY=enforce >> "$Q/queue.env"
printf '%s\n' 'dotnet test {project} -c Release --filter {filter}' > "$Q/accept-templates.txt"
brief queue-template-injection R0 '$ dotnet test tests/Fake.Tests.csproj -c Release --filter Name=example; echo injected'
run_worker
expect test -f "$Q/failed/queue-template-injection.md"
expect test ! -e "$C/codex.calls"
expect test ! -e "$C/accept.calls"
report 'r4 extra template injection fails before worktree/Codex/host execution'

new_case warn-shell
brief warn-shell R0 '$ git diff --check
$ python -B -c "import os,sys; from pathlib import Path; assert not sys.flags.isolated; assert not any(k in os.environ for k in [\"GH_TOKEN\",\"GITHUB_TOKEN\",\"GH_ENTERPRISE_TOKEN\",\"OPENAI_API_KEY\",\"ANTHROPIC_API_KEY\",\"SSH_AUTH_SOCK\"]); Path(os.environ[\"FAKE_CASE\"], \"warn-executed\").touch()" && git diff --check | cat'
echo 'Prebuild: Bootstrap.Beta' >> "$Q/ready/warn-shell.md"
(
  export ACCEPT_POLICY=enforce # A parent environment cannot replace the queue default.
  export GH_TOKEN=SYNTHETIC_TEST_VALUE GITHUB_TOKEN=SYNTHETIC_TEST_VALUE GH_ENTERPRISE_TOKEN=SYNTHETIC_TEST_VALUE
  export OPENAI_API_KEY=SYNTHETIC_TEST_VALUE ANTHROPIC_API_KEY=SYNTHETIC_TEST_VALUE SSH_AUTH_SOCK=SYNTHETIC_TEST_VALUE
  run_worker
)
expect test -f "$Q/done/warn-shell.md"
expect test -f "$C/warn-executed"
expect test "$(wc -l < "$Q/work/accept-violations.log")" -eq 2
expect grep -Eq '^[0-9TZ:-]+[[:space:]]+warn-shell.md[[:space:]]+Accept line=9[[:space:]]+Accept contains forbidden' "$Q/work/accept-violations.log"
expect grep -q 'Prebuild line=11' "$Q/work/accept-violations.log"
expect grep -q 'template=legacy-shell, exit=0' "$Q/work/warn-shell-accept.txt"
expect test "$(wc -l < "$C/prebuild.calls")" -eq 1
if grep -q 'warn-executed\|SYNTHETIC_TEST_VALUE' "$Q/work/accept-violations.log"; then exit 1; fi
# The original brief contains the inline marker; appended summaries must not repeat it.
expect test "$(grep -c warn-executed "$Q/done/warn-shell.md")" -eq 1
report 'r5 unset policy warns once per Accept/Prebuild violation, runs legacy shell and keeps credential reduction'

new_case warn-pipeline-pass
echo ACCEPT_POLICY=warn >> "$Q/queue.env"
brief warn-pipeline-pass R0 '$ dotnet test tests/Fake.Tests.csproj | cat >/dev/null; true'
run_worker
expect test -f "$Q/done/warn-pipeline-pass.md"
expect grep -q 'template=legacy-shell, exit=0, tests=2' "$Q/work/warn-pipeline-pass-accept.txt"
expect test "$(wc -l < "$Q/work/accept-violations.log")" -eq 1
expect test "$(find "$Q/work/warn-pipeline-pass-accept-1-tests" -type f | wc -l)" -eq 1
for mode in zero absent skipped nonzero utf8; do
  new_case "warn-evidence-$mode"
  case "$mode" in
    zero) echo 'Passed! - Failed: 0, Passed: 0, Skipped: 0, Total: 0' > "$C/test-output";;
    absent) echo 'Build succeeded.' > "$C/test-output";;
    skipped) echo 'Passed! - Failed: 0, Passed: 0, Skipped: 2, Total: 2' > "$C/test-output";;
    nonzero) echo 8 > "$C/test-exit";;
    utf8) printf '\377\n' > "$C/test-output";;
  esac
  brief "warn-evidence-$mode" R0 '$ dotnet test tests/Fake.Tests.csproj | cat >/dev/null; true'
  run_worker
  expect test -f "$Q/failed/warn-evidence-$mode.md"
  expect grep -q 'exit=0, tests=0' "$Q/work/warn-evidence-$mode-accept.txt"
  removed "warn-evidence-$mode"
done
report 'r5 shell pipelines retain every dotnet test output/exit; zero/skipped/unknown/nonzero/bad UTF-8 fail'

for mode in exit pipeline; do
  new_case "warn-$mode"
  command='$ false'; [ "$mode" != pipeline ] || command='$ false | true'
  brief "warn-$mode" R0 "$command"
  run_worker
  expect test -f "$Q/failed/warn-$mode.md"
  expect grep -q 'template=legacy-shell, exit=1' "$Q/work/warn-$mode-accept.txt"
  expect test "$(wc -l < "$Q/work/accept-violations.log")" -eq 1
done
new_case warn-prebuild-fail
brief warn-prebuild-fail R0 '$ git diff --check'
echo 'Prebuild: Bootstrap.Beta Desktop' >> "$Q/ready/warn-prebuild-fail.md"
touch "$C/prebuild-fail"
run_worker
expect test -f "$Q/failed/warn-prebuild-fail.md"
expect test ! -e "$C/codex.calls"
expect test "$(wc -l < "$C/prebuild.calls")" -eq 1
expect test "$(wc -l < "$Q/work/accept-violations.log")" -eq 1
new_case enforce-prebuild
echo ACCEPT_POLICY=enforce >> "$Q/queue.env"
brief enforce-prebuild R0 '$ git diff --check'
echo 'Prebuild: Bootstrap.Beta' >> "$Q/ready/enforce-prebuild.md"
run_worker
expect test -f "$Q/failed/enforce-prebuild.md"
expect test ! -e "$C/codex.calls"
expect test ! -e "$C/accept.calls"
expect test ! -e "$Q/work/accept-violations.log"
report 'r5 shell exit/pipeline and Prebuild errors still fail; enforce rejects before execution as r4'

new_case host-execution
brief host-execution R0 '$ dotnet build tests/Fake.Tests.csproj --no-restore
$ dotnet test tests/Fake.Tests.csproj --no-restore --no-build
$ pwsh -NoProfile -File ./scripts/verify.ps1 -StructureOnly
$ git diff --check'
echo 'Prebuild: Desktop Bootstrap' >> "$Q/ready/host-execution.md"
echo "SETUP='for name in GH_TOKEN GITHUB_TOKEN GH_ENTERPRISE_TOKEN OPENAI_API_KEY ANTHROPIC_API_KEY SSH_AUTH_SOCK; do if [[ -v \"\$name\" ]]; then exit 42; fi; done; echo setup-ok'" >> "$Q/queue.env"
(
  export GH_TOKEN=SYNTHETIC_TEST_VALUE GITHUB_TOKEN=SYNTHETIC_TEST_VALUE GH_ENTERPRISE_TOKEN=SYNTHETIC_TEST_VALUE
  export OPENAI_API_KEY=SYNTHETIC_TEST_VALUE ANTHROPIC_API_KEY=SYNTHETIC_TEST_VALUE SSH_AUTH_SOCK=SYNTHETIC_TEST_VALUE
  run_worker
)
expect test -f "$Q/done/host-execution.md"
expect grep -q setup-ok "$Q/work/host-execution-setup.log"
expect test "$(wc -l < "$C/prebuild.calls")" -eq 2
expect grep -q 'template=pwsh-script, exit=0' "$Q/work/host-execution-accept.txt"
report 'U5 trusted SETUP/Prebuild/dotnet/pwsh execute with all six environment variables removed'

new_case prebuild-failure
brief prebuild-failure R0 "$GOOD"
echo 'Prebuild: Desktop Bootstrap' >> "$Q/ready/prebuild-failure.md"
touch "$C/prebuild-fail"
run_worker
expect test -f "$Q/failed/prebuild-failure.md"
expect test ! -e "$C/codex.calls"
expect test "$(wc -l < "$C/prebuild.calls")" -eq 1
report 'U5 failed Prebuild stops before the next build and Codex'

new_case quota-fresh
printf '%s\n' '{"used_percent":100,"sample_age_min":10,"status":"EXHAUSTED"}' > "$C/quota-output"
brief quota-fresh R0 '$ git diff --check'
run_worker
expect test -f "$Q/stop"
expect test -f "$Q/ready/quota-fresh.md"
expect test ! -e "$C/codex.calls"
expect grep -q 'quota exhausted (fresh sample): queue stopped' "$Q/work/workers.log"
report 'quota fresh 100 percent stops the queue before claim'

new_case quota-stale
printf '%s\n' '{"used_percent":100,"sample_age_min":10.01,"status":"EXHAUSTED"}' > "$C/quota-output"
brief quota-stale R0 '$ git diff --check'
run_worker
expect test -f "$Q/done/quota-stale.md"
report 'quota stale 100 percent allows a task to refresh the sample'

for mode in failed unreadable missing; do
  new_case "quota-$mode"
  case "$mode" in
    failed) touch "$C/quota-fail";;
    unreadable) printf '%s\n' '{"status":"NO_DATA"}' > "$C/quota-output";;
    missing) echo "QUOTA_CHECK='false'" >> "$Q/queue.env";;
  esac
  brief "quota-$mode" R0 '$ git diff --check'
  run_worker
  expect test -f "$Q/ready/quota-$mode.md"
  expect test ! -e "$C/codex.calls"
  expect grep -q 'warning: quota check failed or unreadable; not claiming; retry in 120 s' "$Q/work/workers.log"
  expect grep -qx 120 "$C/sleep.calls"
done
new_case quota-retry
printf '%s\n' '{"status":"NO_DATA"}' > "$C/quota-output"
touch "$C/quota-retry"
brief quota-retry R0 '$ git diff --check'
run_worker
expect test -f "$Q/done/quota-retry.md"
expect test "$(wc -l < "$C/quota.calls")" -ge 2
report 'quota failures leave ready intact, wait 120 seconds and retry successfully'

new_case quota-credits
echo ALLOW_CREDITS=1 >> "$Q/queue.env"
touch "$C/quota-fail"
brief quota-credits R0 '$ git diff --check'
run_worker
expect test -f "$Q/done/quota-credits.md"
expect test ! -e "$C/quota.calls"
report 'ALLOW_CREDITS=1 bypasses the quota command and gate'

new_case quota-per-claim
brief quota-first R0 '$ git diff --check'
brief quota-second R0 '$ git diff --check'
touch -t 202001010000 "$Q/ready/quota-first.md"
touch "$C/quota-exhaust-after-task"
run_worker
expect test -f "$Q/done/quota-first.md"
expect test -f "$Q/ready/quota-second.md"
expect test -f "$Q/stop"
expect test "$(wc -l < "$C/quota.calls")" -eq 2
new_case usage-limit
echo 'usage limit' > "$C/final-output"
brief usage-limit R0 '$ git diff --check'
run_worker
expect test -f "$Q/failed/usage-limit.md"
expect test -f "$Q/stop"
expect grep -q 'usage limit: queue stopped' "$Q/work/workers.log"
report 'quota rechecks before each claim and shares the existing usage-limit stop route'

new_case cache-and-summary
echo "EXTRA_ADD_DIRS='$(cygpath -m "$T")/shared/nuget;$(cygpath -m "$T")/shared/python'" >> "$Q/queue.env"
echo SENTINEL_PRIVATE > "$C/final-output"
brief cache-and-summary R0 '$ git diff --check'
run_worker
expect test -f "$Q/done/cache-and-summary.md"
expect test "$(cat "$C/add-dirs")" = "$(cygpath -w "$Q/work/cache-and-summary-tmp")"
expect test -d "$Q/work/cache-and-summary-tmp"
expect grep -q SENTINEL_PRIVATE "$Q/work/cache-and-summary-out.md"
if grep -q SENTINEL_PRIVATE "$Q/done/cache-and-summary.md"; then exit 1; fi
report 'U4/U7 only task temp gets write access; raw output is not copied to briefs'

for path in .github/workflows/new.yml src/Logic.cs packages.lock.json; do
  n=risk-$(printf %s "$path" | md5sum | cut -c1-6)
  new_case "$n"
  echo "$path" > "$C/edit-path"
  brief "$n" R0 '$ git diff --check'
  sed -i "s@Scope: tracked.txt@Scope: $path@" "$Q/ready/$n.md"
  run_worker
  expect test -f "$Q/failed/$n.md"
  expect grep -q 'host risk floor' "$Q/failed/$n.md"
  expect test -f "$T/wt/q-$(printf %s "$n" | md5sum | cut -c1-6)/$path"
  expect test ! -e "$C/accept.calls"
done
report 'U2 brief R0 cannot downgrade protected or executable/lock-file diffs'

for state in proposed running done failed; do
  new_case "duplicate-$state"
  brief "duplicate-$state" R0 '$ git diff --check'
  echo preserve-old > "$Q/$state/duplicate-$state.md"
  cp "$Q/ready/duplicate-$state.md" "$C/original"
  run_worker
  expect cmp -s "$C/original" "$Q/ready/duplicate-$state.md"
  expect test "$(cat "$Q/$state/duplicate-$state.md")" = preserve-old
  expect test ! -e "$C/codex.calls"
done
report 'U9 all-state duplicate claims leave both old and new briefs untouched'

new_case duplicate-race
brief duplicate-race R0 '$ git diff --check'
run_worker & p1=$!
run_worker & p2=$!
wait "$p1"; wait "$p2"
expect test "$(wc -l < "$C/codex.calls")" -eq 1
expect test -f "$Q/done/duplicate-race.md"
new_case result-race
touch "$C/result-race"
brief result-race R0 '$ git diff --check'
run_worker
expect test "$(cat "$Q/done/result-race.md")" = preserve-old-result
expect test -f "$Q/running/result-race.md"
if grep -q '## Result' "$Q/running/result-race.md"; then exit 1; fi
report 'U9 concurrent same-task claim runs once; result collision retains running and prior result'

new_case collision
expect test "$(printf audit-task-322 | md5sum | cut -c1-6)" = "$(printf audit-task-5009 | md5sum | cut -c1-6)"
collision="$T/wt/q-0b13d7"
"$REAL_GIT" -C "$T/repo" worktree add -q -b feature/queue/audit-task-322 "$collision" main
echo original-work > "$collision/sentinel.txt"
brief audit-task-5009 R0 '$ git diff --check'
run_worker
expect test -f "$Q/failed/audit-task-5009.md"
expect test "$(cat "$collision/sentinel.txt")" = original-work
expect test "$("$REAL_GIT" -C "$collision" branch --show-current)" = feature/queue/audit-task-322
new_case failed-add
touch "$C/fail-add"
brief failed-add R0 '$ git diff --check'
run_worker
expect test -f "$Q/failed/failed-add.md"
expect test -f "$T/wt/q-$(printf failed-add | md5sum | cut -c1-6)/partial.txt"
report 'U11 known hash collision and partially failed add never delete existing work'

new_case failed-commit
touch "$C/fail-commit"
brief failed-commit R0 '$ git diff --check'
run_worker
expect test -f "$Q/failed/failed-commit.md"
expect test ! -e "$Q/done/failed-commit.md"
retained="$T/wt/q-$(printf failed-commit | md5sum | cut -c1-6)"
expect grep -q 'codex edit' "$retained/tracked.txt"
expect grep -q 'codex edit' "$Q/work/failed-commit.patch"
expect test -n "$("$REAL_GIT" -C "$retained" status --porcelain)"
new_case no-change
touch "$C/no-edit"
brief no-change R0 '$ git diff --check'
run_worker
expect test -f "$Q/done/no-change.md"
expect test "$(cat "$Q/work/no-change-head.txt")" = "$("$REAL_GIT" -C "$T/repo" rev-parse main)"
report 'U10 commit failure retains changes and fails; legitimate unchanged task completes'

new_case lock-authorized
echo packages.lock.json > "$C/edit-path"
brief lock-authorized R2 '$ git diff --check'
sed -i 's/Scope: tracked.txt/Scope: packages.lock.json/' "$Q/ready/lock-authorized.md"
run_worker
expect test -f "$Q/done/lock-authorized.md"
expect test "$("$REAL_GIT" -C "$T/repo" show feature/queue/lock-authorized:packages.lock.json | tail -1)" = 'codex edit'
new_case lock-outside-scope
echo packages.lock.json > "$C/edit-path"
brief lock-outside-scope R2 '$ git diff --check'
run_worker
expect test -f "$Q/failed/lock-outside-scope.md"
expect grep -q 'codex edit' "$T/wt/q-$(printf lock-outside-scope | md5sum | cut -c1-6)/packages.lock.json"
new_case accept-dirty
touch "$C/accept-dirty"
brief accept-dirty R0 '$ git diff --check'
run_worker
expect test -f "$Q/done/accept-dirty.md"
expect test ! -e "$Q/failed/accept-dirty.md"
expect grep -q '^cleanup pending:' "$Q/done/accept-dirty.md"
expect grep -q '^accept-dirty' "$Q/work/cleanup-pending.log"
expect test "$(cat "$T/wt/q-$(printf accept-dirty | md5sum | cut -c1-6)/packages.lock.json")" = 'accept changed lock'
expect grep -q 'accept changed lock' "$Q/work/accept-dirty.patch"
report 'U10/N3 authorized lock edits commit; scope violations fail; successful Accept with dirty cleanup remains done'

new_case chain-sha
sha=$("$REAL_GIT" -C "$T/repo" rev-parse main)
"$REAL_GIT" -C "$T/repo" branch feature/queue/pinned-parent "$sha"
printf '## Result (done)\nBranch: feature/queue/pinned-parent; head: %s;\n' "$sha" > "$Q/done/pinned-parent.md"
echo "$sha" > "$Q/work/pinned-parent-head.txt"
# Move the branch after its accepted result; the child must still start at the recorded SHA.
"$REAL_GIT" -C "$T/repo" branch -f feature/queue/pinned-parent feature/queue/lock-authorized
touch "$C/no-edit"
brief pinned-child R0 '$ git diff --check' feature/queue/pinned-parent
run_worker
expect test -f "$Q/done/pinned-child.md"
expect test "$(cat "$Q/work/pinned-child-head.txt")" = "$sha"
new_case chain-missing-sha
"$REAL_GIT" -C "$T/repo" branch feature/queue/no-sha main
touch "$Q/done/no-sha.md"
brief no-sha-child R0 '$ git diff --check' feature/queue/no-sha
run_worker
expect test -f "$Q/ready/no-sha-child.md"
expect test ! -e "$C/codex.calls"
report 'U13 dependency uses accepted SHA despite branch movement; missing SHA blocks'

new_case invalid-input
brief invalid-schema R0 '$ git diff --check'
sed -i '1i SchemaVersion: 99' "$Q/ready/invalid-schema.md"
printf 'Title: \377\nRisk: R0\nAccept:\n$ git diff --check\n' > "$Q/ready/invalid-utf8.md"
run_worker
expect test -f "$Q/failed/invalid-schema.md"
expect test -f "$Q/failed/invalid-utf8.md"
expect test ! -e "$C/codex.calls"
report 'U12/42 malformed UTF-8 and unknown fields stop before Codex'

# Exercise the real wrapper with an in-process fake codex function; no CLI/network.
"$REAL_PWSH" -NoProfile -File "$(cygpath -w "$BIN/tests/test-wrapper.ps1")" \
  -Wrapper "$(cygpath -w "$BIN/run-codex-ws.ps1")" -Fixture "$(cygpath -w "$T")"
report 'N1 wrapper preserves pre-existing caches in read-only and writable tasks'

# Real nested gitlinks, populated from local synthetic repositories without submodule
# update/clone/config commands. No remote URLs, SDKs or product data are used.
"$REAL_GIT" init -q -b main "$T/submodule-fixture/nested"
echo baseline > "$T/submodule-fixture/nested/tracked.txt"
"$REAL_GIT" -C "$T/submodule-fixture/nested" add tracked.txt
"$REAL_GIT" -C "$T/submodule-fixture/nested" commit -q -m nested-baseline
nested_sha=$("$REAL_GIT" -C "$T/submodule-fixture/nested" rev-parse HEAD)
echo alternate >> "$T/submodule-fixture/nested/tracked.txt"
"$REAL_GIT" -C "$T/submodule-fixture/nested" commit -qam nested-alternate
"$REAL_GIT" -C "$T/submodule-fixture/nested" rev-parse HEAD > "$T/nested-other-head"
"$REAL_GIT" -C "$T/submodule-fixture/nested" reset -q --hard "$nested_sha"
cp "$T/submodule-fixture/nested/.git/config" "$T/nested-config"
"$REAL_GIT" init -q -b main "$T/submodule-fixture"
printf '[submodule "nested"]\n\tpath = nested\n\turl = ../nested-fixture\n\tignore = all\n' > "$T/submodule-fixture/.gitmodules"
echo baseline > "$T/submodule-fixture/tracked.txt"
"$REAL_GIT" -C "$T/submodule-fixture" add tracked.txt .gitmodules
"$REAL_GIT" -C "$T/submodule-fixture" update-index --add --cacheinfo "160000,$nested_sha,nested"
"$REAL_GIT" -C "$T/submodule-fixture" commit -q -m submodule-baseline
module_sha=$("$REAL_GIT" -C "$T/submodule-fixture" rev-parse HEAD)
echo alternate >> "$T/submodule-fixture/tracked.txt"
"$REAL_GIT" -C "$T/submodule-fixture" commit -qam submodule-alternate
"$REAL_GIT" -C "$T/submodule-fixture" rev-parse HEAD > "$T/module-other-head"
"$REAL_GIT" -C "$T/submodule-fixture" reset -q --hard "$module_sha"
cp "$T/submodule-fixture/.git/config" "$T/module-config"
printf '[submodule "example"]\n\tpath = example\n\turl = ./submodule-fixture\n\tignore = all\n' > "$T/repo/.gitmodules"
"$REAL_GIT" -C "$T/repo" add .gitmodules
"$REAL_GIT" -C "$T/repo" update-index --add --cacheinfo "160000,$module_sha,example"
"$REAL_GIT" -C "$T/repo" commit -q -m parent-gitlink

new_case submodule-clean
touch "$C/populate-submodule" "$C/check-plain-remove"
brief submodule-clean R0 "$GOOD"
run_worker
expect test -f "$Q/done/submodule-clean.md"
expect test ! -e "$Q/failed/submodule-clean.md"
expect grep -q 'working trees containing submodules cannot be moved or removed' "$C/plain-remove.log"
expect grep -q 'worktree remove --force ' "$C/remove.calls"
expect test ! -e "$Q/work/cleanup-pending.log"
removed submodule-clean
report 'fix-wt-remove clean nested submodules: real plain remove fails; force removes; Accept/commit remain done'

for mode in tracked untracked head nested-tracked nested-head uninitialized; do
  n="submodule-$mode"
  new_case "$n"
  touch "$C/populate-submodule"
  case "$mode" in
    nested-*) printf 'example/nested %s\n' "${mode#nested-}" > "$C/submodule-change"; cp "$T/nested-other-head" "$C/other-head";;
    *) printf 'example %s\n' "$mode" > "$C/submodule-change"; cp "$T/module-other-head" "$C/other-head";;
  esac
  brief "$n" R0 "$GOOD"
  run_worker
  retained="$T/wt/q-$(printf %s "$n" | md5sum | cut -c1-6)"
  expect test -d "$retained"
  expect test -f "$Q/done/$n.md"
  expect test ! -e "$Q/failed/$n.md"
  expect grep -q '^cleanup pending:' "$Q/done/$n.md"
  expect grep -q "^$n" "$Q/work/cleanup-pending.log"
  expect test ! -e "$C/remove.calls"
  expect test -s "$Q/work/$n-status.txt"
  expect test -f "$Q/work/$n.patch"
  expect test -f "$Q/work/$n-untracked.tar"
  case "$mode" in
    *tracked)
      if [ "$mode" = untracked ]; then
        expect "$REAL_TAR" -tf "$Q/work/$n-untracked.tar" example/new.txt
        expect test "$("$REAL_TAR" -xOf "$Q/work/$n-untracked.tar" example/new.txt)" = 'submodule evidence'
      else expect grep -q 'submodule evidence' "$Q/work/$n.patch"; fi;;
    *head) expect grep -q "$(cat "$C/other-head")" "$Q/work/$n-status.txt";;
  esac
  expect test "$("$REAL_PYTHON" -I -B "$BIN/q.py" required-free "$Q" 215 20)" -eq 240
done
report 'fix-wt-remove tracked/untracked/nested edits, mismatched HEAD and uninitialized submodules retain evidence/reserve; done'

new_case cleanup-remove-fail
touch "$C/populate-submodule" "$C/fail-remove"
brief cleanup-remove-fail R0 "$GOOD"
run_worker
retained="$T/wt/q-$(printf cleanup-remove-fail | md5sum | cut -c1-6)"
expect test -d "$retained"
expect test -f "$Q/done/cleanup-remove-fail.md"
expect test ! -e "$Q/failed/cleanup-remove-fail.md"
expect grep -q '^cleanup pending: Worktree removal failed' "$Q/done/cleanup-remove-fail.md"
expect grep -q 'synthetic removal failure' "$Q/work/cleanup-remove-fail-git.log"
expect grep -q '^cleanup-remove-fail.*cleanup pending: Worktree removal failed' "$Q/work/cleanup-pending.log"
expect test "$("$REAL_PYTHON" -I -B "$BIN/q.py" required-free "$Q" 215 20)" -eq 240
# A retained done tree must still prevent the next claim at 239 GiB.
brief cleanup-disk-block R0 "$GOOD"
echo $((239*1048576)) > "$C/free"
rm -- "$Q/stop"
run_worker
expect test -f "$Q/ready/cleanup-disk-block.md"
expect test "$(wc -l < "$C/codex.calls")" -eq 1
expect cmp -s "$T/module-config" "$retained/example/.git/config"
expect cmp -s "$T/nested-config" "$retained/example/nested/.git/config"
report 'fix-wt-remove injected removal failure stays done, logs pending, reserves disk and blocks next claim'

expect cmp -s "$T/initial-config" "$T/repo/.git/config"
cleanup
printf 'ALL PASS P0: %s groups; offline fixtures cleaned; git config unchanged\n' "$passed"
