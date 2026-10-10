# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Codex queue helper: brief parsing, prompt composition, refill splitting.

  python q.py get <brief> <Key>                 print one field of a brief (empty when missing)
  python q.py accept <brief>                    print the Accept commands, one per line
  python q.py check <brief>                     validate Risk, literal Scope and Accept templates
  python q.py run-accept <brief> <log-prefix> [base-sha]  run trusted host Accept templates
  python q.py run-prebuild <brief> <log-prefix>  run fixed host Prebuild templates
  python q.py quota                            read one JSON sample from stdin; exit 3 if exhausted
  python q.py check-diff <brief> <base-sha>      enforce literal Scope and host-owned risk floors
  python q.py unique <queue> <name> [source]    caller holds the claim lock
  python q.py chain-base <brief> <queue> <base> resolve accepted predecessor SHA
  python q.py required-free <queue> <minimum> <estimate>  required GiB including running/cleanup-pending tasks
  python q.py prompt <brief> <worktree> <branch> <base>   print the Codex prompt for the brief
  python q.py split <refill-out.md> <proposed-dir>        write each "=====BRIEF <slug>=====" block as <slug>.md

A brief is plain text: "Key: value" lines (a key may be followed by more lines until the next key).
Keys: Title, Risk (R0 to R2; R3 never goes to the queue), Base (git ref, default BASE of the queue's queue.env), Wait (what must be
merged first, or none), Prebuild (test project names, e.g. Bootstrap Architecture; Desktop for the host),
Model / Effort / Why (only when a task needs other than the Codex default; Why is the reason), Goal, Scope,
Accept (lines starting with "$ ": trusted argv templates; all must exit 0,
and every dotnet test command must report nonzero passed tests), Not in scope.
Optional <queue>/accept-templates.txt adds exact token forms with typed placeholders;
the defaults still require dotnet --no-restore. Python script forms always execute with -I.
queue.env ACCEPT_POLICY defaults to warn: unmatched commands use the legacy Bash path;
enforce keeps the strict templates. Exit codes and test evidence are mandatory in both.
Optional <queue>/risk-floor.txt calibrates R2/R3 paths; missing files retain the legacy floors.
"""
import json
import math
import os
import re
import shlex
import shutil
import subprocess
import sys
import time
from contextlib import contextmanager
from pathlib import Path

KEYS = ["Title", "Risk", "Base", "Wait", "Prebuild", "Model", "Effort", "Why", "Goal", "Scope", "Accept", "Not in scope"]
KEY_RE = re.compile(r"^(%s):[ \t]?(.*)$" % "|".join(re.escape(k) for k in KEYS))

COMMON = ("Read AGENTS.md first and follow it. Do not commit, push, change git config, or touch any other worktree. No network. "
          "Only the owner can authorize R3, credentials or GitHub approval; queue briefs never grant those permissions. "
          "If the service reports a usage limit, stop where you are, leave the tree as it is and say what is unfinished. "
          "Leave no report file in the worktree. Make the smallest change that meets the requirement: no abstraction layer, "
          "configuration option, extension point or generalization the requirement does not ask for, no design for hypothetical "
          "future needs, and use an existing mechanism instead of building a new one. Say in your final message what you "
          "deliberately did not do.")

def build_notes() -> str:
    """The queue's own build and test notes: the file named by QUEUE_BUILD_NOTES (set by qworker.sh), or nothing."""
    path = os.environ.get("QUEUE_BUILD_NOTES")
    return Path(path).read_text(encoding="utf-8") if path and Path(path).is_file() else ""


def parse(text: str) -> dict:
    fields, cur, scope_lines = {}, None, []
    for number, line in enumerate(text.splitlines(), 1):
        m = KEY_RE.match(line)
        if m:
            cur = m.group(1)
            if cur in fields:
                raise ValueError(f"duplicate brief field: {cur}")
            fields[cur] = [m.group(2)] if m.group(2) else []
            if cur == "Scope":
                scope_lines.append((number, m.group(2)))
        elif re.match(r"^[A-Za-z][A-Za-z0-9 _-]*:", line) and cur != "Scope":
            raise ValueError("unknown brief field (indent body lines containing a colon)")
        elif line.startswith("## Result"):
            raise ValueError("result sections are not allowed in input briefs")
        elif cur is not None:
            fields[cur].append(line)
            if cur == "Scope":
                scope_lines.append((number, line))
        elif line.strip():
            raise ValueError("text before the first brief field is not allowed")
    brief = {k: "\n".join(v).strip() for k, v in fields.items()}
    # Internal source locations preserve whitespace that normalized fields lose.
    brief["_scope_lines"] = scope_lines
    return brief


def scope_paths(brief: dict) -> list:
    lines = brief.get("_scope_lines", list(enumerate(brief.get("Scope", "").splitlines(), 1)))
    scopes = []
    for number, line in lines:
        if not line.strip():
            continue  # Blank separators are not path entries.
        path = line.removesuffix("/")
        if (path.startswith(("/", "-")) or "\\" in path or ":" in path
                or any(p in ("", ".", "..") for p in path.split("/"))
                or re.search(r"[\s(){}?*\[\]]", path)):
            raise ValueError(f"Scope line {number}: requires a literal relative file/directory path without whitespace, annotations or globs")
        scopes.append(path)
    if not scopes:
        location = f" line {lines[0][0]}" if lines else ""
        raise ValueError(f"Scope{location}: missing or empty; requires literal relative paths, one per line")
    return scopes


def accept_commands(brief: dict) -> list:
    return [l[2:].strip() for l in brief.get("Accept", "").splitlines() if l.startswith("$ ")]


def command_tokens(command: str) -> list:
    # Apply this before both default and queue templates, including quoted payloads.
    forbidden = [name for marker, name in ((";", "semicolon"), ("|", "pipe"), ("&", "ampersand"),
                 ("<", "redirect"), (">", "redirect"), ("`", "backtick"), ("$(", "command substitution"))
                 if marker in command]
    if forbidden:
        raise ValueError("Accept contains forbidden shell metacharacters: " + ", ".join(dict.fromkeys(forbidden)))
    try:
        args = shlex.split(command)
    except ValueError:
        raise ValueError("Accept has invalid quoting") from None
    if not args:
        raise ValueError("Accept command is empty")
    return args


def relative_path(value: str) -> bool:
    return (bool(re.fullmatch(r"(?:\./)?[A-Za-z0-9_./-]+", value))
            and not value.startswith(("/", "-")) and ".." not in value
            and all(p not in ("", ".", "..") for p in value.removeprefix("./").split("/")))


def placeholder_matches(kind: str, value: str) -> bool:
    if kind in ("{path}", "{paths}"):
        return relative_path(value)
    if kind == "{project}":
        return relative_path(value) and value.endswith((".csproj", ".sln", ".slnx"))
    if kind == "{filter}":
        return bool(re.fullmatch(r"[A-Za-z0-9_. ~!=()+,-]+", value))
    if kind == "{ref}":
        return relative_path(value)
    if kind == "{name}":
        return bool(re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", value))
    return False


def safe_extra_form(args: list) -> None:
    """Even a queue config cannot authorize another interpreter or inline program."""
    placeholders = {"{path}", "{paths}", "{project}", "{filter}", "{ref}", "{name}"}
    if any(("{" in a or "}" in a) and a not in placeholders for a in args):
        raise ValueError("Accept template has an unknown placeholder")
    if "{paths}" in args[:-1]:
        raise ValueError("Accept template {paths} must be last")
    if args[:2] == ["python", "-I"]:
        if args[2:4] == ["-m", "pytest"] and len(args) >= 5:
            path_index = 4  # Only this observed fixed module; never arbitrary -m/-c.
        else:
            path_index = 2
            if len(args) < 3 or not args[2].endswith(".py"):
                raise ValueError("Accept Python template requires a fixed .py script or pytest")
    elif args[0] == "bash":
        path_index = 1
        if len(args) < 2 or not args[1].endswith(".sh"):
            raise ValueError("Accept bash template requires a fixed .sh script")
    elif args[:3] == ["pwsh", "-NoProfile", "-File"]:
        path_index = 3
        if len(args) < 4 or not args[3].endswith(".ps1"):
            raise ValueError("Accept PowerShell template requires a fixed .ps1 script")
    elif args[0] == "test" and len(args) == 3 and args[1] in ("-f", "-s"):
        path_index = 2
    elif args[0] == "dotnet" and len(args) >= 3 and args[1] in ("build", "test", "format", "restore"):
        path_index = 2
        if args[2] != "{project}" and not placeholder_matches("{project}", args[2]):
            raise ValueError("Accept dotnet template requires a relative project")
    else:
        raise ValueError("Accept template interpreter or command is not trusted")
    if args[path_index] not in ("{path}", "{project}") and not relative_path(args[path_index]):
        raise ValueError("Accept template requires relative paths without traversal")
    # Paths anywhere in options must also be repo-relative. Literals are exact, not wildcards.
    for a in args:
        if ".." in a or "\\" in a or "$" in a or ("/" in a and not relative_path(a)):
            raise ValueError("Accept template contains an unsafe path or expansion")
        if re.match(r"^[A-Za-z]:", a):
            raise ValueError("Accept template contains an absolute path")
    if args[0] in ("python", "bash", "pwsh") and any(a in ("-c", "-Command", "-EncodedCommand") for a in args[path_index + 1:]):
        raise ValueError("Accept template cannot use inline interpreter options")


def accept_templates(queue: Path) -> list:
    path = queue / "accept-templates.txt"
    try:
        text = path.read_text(encoding="utf-8-sig")
    except FileNotFoundError:
        return []
    forms = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        try:
            args = command_tokens(line)
            safe_extra_form(args)
        except ValueError as exc:
            raise ValueError(f"accept-templates.txt line {number}: {exc}") from None
        if args not in forms:
            forms.append(args)
    return forms


def accept_policy(queue: Path) -> str:
    # Read only this literal setting; never source queue.env in the helper.
    try:
        text = (queue / "queue.env").read_text(encoding="utf-8-sig")
    except FileNotFoundError:
        return "warn"
    policy = "warn"
    for line in text.splitlines():
        if re.match(r"\s*(?:export\s+)?ACCEPT_POLICY\s*=", line):
            try:
                parts = shlex.split(line.split("=", 1)[1], comments=True)
            except ValueError:
                raise ValueError("ACCEPT_POLICY must be the literal warn or enforce") from None
            if len(parts) > 1 or (parts and parts[0] not in ("", "warn", "enforce")):
                raise ValueError("ACCEPT_POLICY must be the literal warn or enforce")
            policy = parts[0] if parts and parts[0] else "warn"
    return policy


DEFAULT_PROJECT_PREFIX = "Project"


def project_prefix(queue: Path) -> str:
    """Name prefix of the .NET projects that Prebuild builds, e.g. NvtFwCombiner for NvtFwCombiner.Desktop.

    Same rule as accept_policy: read one literal setting, never source queue.env in the helper.
    """
    try:
        text = (queue / "queue.env").read_text(encoding="utf-8-sig")
    except FileNotFoundError:
        return DEFAULT_PROJECT_PREFIX
    prefix = DEFAULT_PROJECT_PREFIX
    for line in text.splitlines():
        if re.match(r"\s*(?:export\s+)?PROJECT_PREFIX\s*=", line):
            try:
                parts = shlex.split(line.split("=", 1)[1], comments=True)
            except ValueError:
                raise ValueError("PROJECT_PREFIX must be a plain project name") from None
            if len(parts) > 1 or (parts and parts[0] and not re.fullmatch(r"[A-Za-z][A-Za-z0-9_.]{0,79}", parts[0])):
                raise ValueError("PROJECT_PREFIX must be a plain project name")
            prefix = parts[0] if parts and parts[0] else DEFAULT_PROJECT_PREFIX
    return prefix


def extra_argv(args: list, forms: list) -> list | None:
    candidate = args[:]
    # Legacy Python script spelling is accepted, but never executes without isolation.
    if candidate[0] == "python" and candidate[1:2] != ["-I"]:
        candidate.insert(1, "-I")
    for form in forms:
        variadic = form[-1] == "{paths}"
        if (len(candidate) < len(form) if variadic else len(candidate) != len(form)):
            continue
        fixed = form[:-1] if variadic else form
        if all(placeholder_matches(t, v) if t.startswith("{") else t == v
               for t, v in zip(fixed, candidate)) and (
                not variadic or all(relative_path(v) for v in candidate[len(fixed):])):
            # -I ignores PYTHONDONTWRITEBYTECODE too; preserve the worker's no-cache policy.
            return [*candidate[:2], "-B", *candidate[2:]] if candidate[0] == "python" else candidate
    return None


def accept_argv(command: str, templates: list = ()) -> list:
    """Default forms plus typed queue forms; never pass brief text to a shell."""
    args = command_tokens(command)
    extra = extra_argv(args, templates)
    if extra is not None:
        return extra
    return default_accept_argv(args)


def default_accept_argv(args: list) -> list:
    if args == ["git", "diff", "--check"]:
        return args
    if args[:3] == ["pwsh", "-NoProfile", "-File"] and len(args) >= 4:
        script = args[3]
        if (not re.fullmatch(r"\./scripts/[A-Za-z0-9_./-]+\.ps1", script)
                or not relative_path(script)):
            raise ValueError("Accept requires a relative ./scripts/*.ps1 path without traversal")
        # Existing queue switches only; no arbitrary arguments or PowerShell expressions.
        if any(a not in {"-SkipStopApp", "-StructureOnly", "-UseNoAppHost", "-SelfTest"} for a in args[4:]):
            raise ValueError("Accept PowerShell option is not trusted")
        return args
    if len(args) < 3 or args[:2] not in (["dotnet", "build"], ["dotnet", "test"]):
        raise ValueError("Accept command is not a trusted template")
    project = args[2]
    if (not re.fullmatch(r"[A-Za-z0-9_./-]+\.(?:csproj|sln|slnx)", project)
            or not relative_path(project) or project.startswith("./")):
        raise ValueError("Accept requires a relative project path without traversal")
    flags = {"--no-restore", "--nologo", "-nologo", "-p:UseSharedCompilation=false",
             "-p:UseAppHost=false", "-nodeReuse:false", "-nr:false", "-m:1"}
    if args[1] == "test":
        flags.add("--no-build")
    i = 3
    while i < len(args):
        arg = args[i]
        if arg in flags:
            i += 1
        elif arg in ("-c", "--configuration", "-v", "--verbosity", "--filter") and i + 1 < len(args):
            value = args[i + 1]
            if arg in ("-c", "--configuration"):
                valid = value in ("Release", "Debug")
            elif arg in ("-v", "--verbosity"):
                valid = value in ("q", "quiet", "m", "minimal", "n", "normal")
            else:
                valid = args[1] == "test" and placeholder_matches("{filter}", value)
            if not valid:
                raise ValueError("Accept option value is not trusted")
            i += 2
        elif args[1] == "test" and args[i:] == ["--", "RunConfiguration.TreatNoTestsAsError=true"]:
            i = len(args)
        else:
            raise ValueError("Accept option is not trusted")
    if "--no-restore" not in args:
        raise ValueError("Accept dotnet commands require --no-restore")
    return args


def validate(brief: dict, templates: list = (), policy: str = "enforce") -> list:
    if policy not in ("warn", "enforce"):
        raise ValueError("ACCEPT_POLICY must be the literal warn or enforce")
    risk = brief.get("Risk", "")
    if risk.startswith("R3"):
        raise ValueError("R3 never runs from the queue")
    if not re.fullmatch(r"R[012](?:[ \t]+[^\r\n]+)?", risk):
        raise ValueError("Risk is missing, empty or invalid (expected R0, R1 or R2)")
    scope_paths(brief)  # Scope syntax is mandatory even in Accept warning mode.
    if not brief.get("Accept", ""):
        raise ValueError("Accept is missing or empty")
    commands = accept_commands(brief)
    if not commands or any(not c for c in commands) or any(l.strip() == "$" for l in brief["Accept"].splitlines()):
        raise ValueError("Accept requires nonempty '$ ' commands")
    argv = []
    for command in commands:
        try:
            argv.append(accept_argv(command, templates))
        except ValueError:
            if policy == "enforce":
                raise
            argv.append(command)
    prebuild_commands(brief, policy)
    return argv


def prebuild_commands(brief: dict, policy: str = "enforce", prefix: str = DEFAULT_PROJECT_PREFIX) -> list:
    commands = []
    for name in brief.get("Prebuild", "").split():
        valid = re.fullmatch(r"[A-Za-z][A-Za-z0-9_]{0,79}", name)
        if not valid and policy == "enforce":
            raise ValueError("Prebuild requires plain project names without paths or options")
        project = (f"src/{prefix}.Desktop/{prefix}.Desktop.csproj" if name == "Desktop" else
                   f"tests/{prefix}.{name}.Tests/{prefix}.{name}.Tests.csproj")
        args = ["dotnet", "build", project, "-c", "Release", "-v", "q", "-nologo",
                "-p:UseSharedCompilation=false", "-nodeReuse:false"]
        # Old bin splits project names, then quotes the expanded project path.
        commands.append(args if valid else shlex.join(args))
    return commands


HOST_SECRET_VARS = {"GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "OPENAI_API_KEY",
                    "ANTHROPIC_API_KEY", "SSH_AUTH_SOCK"}


def host_env() -> dict:
    # Risk reduction only: host programs can still read files, keyrings and other credentials.
    return {k: v for k, v in os.environ.items() if k.upper() not in HOST_SECRET_VARS}


def quota_result(text: str) -> int:
    if len(text.strip().splitlines()) != 1:
        raise ValueError("quota sample must be one JSON line")
    sample = json.loads(text)
    if not isinstance(sample, dict) or sample.get("status") not in ("OK", "EXHAUSTED"):
        raise ValueError("quota sample is unavailable")
    used, age = sample.get("used_percent"), sample.get("sample_age_min")
    if any(type(v) not in (int, float) or not math.isfinite(v) or v < 0 for v in (used, age)):
        raise ValueError("quota sample has invalid usage or age")
    return 3 if used >= 100 and age <= 10 else 0


def risk_rules(queue: Path) -> dict | None:
    """Host-owned queue rules; None means the unchanged conservative legacy policy."""
    try:
        text = (queue / "risk-floor.txt").read_text(encoding="utf-8-sig")
    except FileNotFoundError:
        return None
    rules = dict.fromkeys((".git", ".github", ".agents", ".codex", "governance", "firmware",
                           "externaltools", "release", "releases", "signing", "permissions",
                           "agents.md", "claude.md", "*.bin", "*.hex", "*.elf", "*.srec", "*.pfx", "*.pem"), 3)
    seen = set()
    for number, line in enumerate(text.splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        match = re.fullmatch(r"R([23])[ \t]+(\S+)", line.strip())
        rule = match[2].lower() if match else ""
        if rule.startswith("*."):
            valid = bool(re.fullmatch(r"\*\.[a-z0-9]+(?:\.[a-z0-9]+)*", rule))
        elif rule.startswith("name-suffix:"):
            valid = bool(re.fullmatch(r"name-suffix:[a-z0-9_.-]+", rule)) and ".." not in rule
        else:
            valid = relative_path(rule.removesuffix("/")) and not rule.startswith("./")
        if not match or not valid or rule in seen:
            raise ValueError(f"risk-floor.txt line {number}: expected unique R3/R2 path, directory name, *.ext or name-suffix:value")
        seen.add(rule)
        # Only the identical rule replaces a built-in. Other matching R3 rules still win.
        rules[rule] = int(match[1])
    return rules


def risk_floor(path: str, rules: dict | None = None) -> int:
    """Brief text cannot exempt paths; queue config is trusted host input."""
    path = path.lower()
    parts = path.split("/")
    floor = 0 if parts[-1].endswith((".md", ".txt")) else 2
    if rules is not None:
        for rule, level in rules.items():
            if rule.startswith("name-suffix:"):
                matches = any(p.endswith(rule[len("name-suffix:"):]) for p in parts)
            elif rule.startswith("*."):
                matches = parts[-1].endswith(rule[1:])
            elif "/" in rule:
                matches = path == rule.rstrip("/") or path.startswith(rule.rstrip("/") + "/")
            else:
                matches = rule in parts
            if matches:
                floor = max(floor, level)
        return floor
    # Compatibility fallback only: without a queue file do not silently relax protection.
    protected = {".git", ".github", ".agents", ".codex", "governance", "firmware", "externaltools",
                 "profiles", "release", "releases", "signing", "permissions", "contracts", "schemas",
                 "eng", "scripts"}
    if (protected.intersection(parts) or parts[-1] in {"agents.md", "claude.md", "spec.md", "product-spec.md"}
            or any(p.endswith((".domain", ".profiles")) for p in parts)
            or parts[-1].endswith((".bin", ".hex", ".elf", ".srec", ".pfx", ".pem"))):
        return 3
    return floor


def check_diff(brief: dict, base: str, templates: list = (), policy: str = "enforce",
               rules: dict | None = None) -> None:
    validate(brief, templates, policy)
    if not re.fullmatch(r"[0-9a-f]{40,64}", base):
        raise ValueError("diff base must be a full commit SHA")
    scopes = scope_paths(brief)
    paths = set()
    # Include committed, staged, unstaged, deleted/renamed sources, and untracked paths.
    for args in (["diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--name-only", "-z", base],
                 ["ls-files", "--others", "--exclude-standard", "-z"]):
        result = subprocess.run(["git", *args], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=True)
        paths.update(p for p in result.stdout.decode("utf-8").split("\0") if p)
    risk = int(brief["Risk"][1])
    for path in paths:
        if not any(path == s or path.startswith(s + "/") for s in scopes):
            raise ValueError("diff contains a path outside Scope; worktree retained")
        floor = risk_floor(path, rules)
        if floor == 3 or risk < floor:
            raise ValueError("diff exceeds the host risk floor or touches a protected R3 path; worktree retained")


STATES = ("proposed", "ready", "running", "done", "failed")


def task_name(slug: str, queue: Path) -> None:
    # Bound the path including evidence suffixes, with margin below Windows MAX_PATH.
    if (not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,79}", slug) or slug.endswith(".")
            or ".." in slug or re.match(r"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", slug, re.I)
            or len(str(queue.resolve() / "work" / (slug + "-untracked.tar"))) >= 240):
        raise ValueError("invalid Windows task name or task path too long")


def unique_task(queue: Path, slug: str, source: Path | None = None) -> None:
    task_name(slug, queue)
    for state in STATES:
        for path in (queue / state).glob("*.md"):
            if path.name.casefold() == (slug + ".md").casefold() and path != source:
                raise ValueError("task name already exists in a queue state")
    if any(p.name.casefold().startswith(slug.casefold() + "-") for p in (queue / "work").glob("*")):
        raise ValueError("task name has retained work evidence; do not reuse it")


@contextmanager
def claim_lock(queue: Path):
    lock = queue.parent / ".qworker-claim.lock"
    for attempt in range(60):
        try:
            lock.mkdir()
            break
        except FileExistsError:
            time.sleep(1)
    else:
        raise ValueError("claim lock unavailable; manual review if stale")
    try:
        yield
    finally:
        lock.rmdir()


def chain_base(brief: dict, queue: Path, default: str) -> str:
    ref = brief.get("Base") or default
    ref = ref.removeprefix("refs/heads/")
    if not ref.startswith("feature/queue/"):
        return ref
    parent = ref.removeprefix("feature/queue/")
    task_name(parent, queue)
    done = queue / "done" / (parent + ".md")
    if not done.is_file() or any((queue / s / (parent + ".md")).exists() for s in ("proposed", "failed", "running", "ready")):
        raise ValueError("predecessor must be done")
    sha = (queue / "work" / (parent + "-head.txt")).read_text(encoding="utf-8").strip()
    result = done.read_text(encoding="utf-8").split("## Result (done)\n")
    if (not re.fullmatch(r"[0-9a-f]{40,64}", sha) or len(result) != 2
            or not re.search(r"^Branch: " + re.escape(ref) + r"; head: " + sha + r";", result[1], re.M)):
        raise ValueError("predecessor has no matching accepted SHA evidence")
    for args in (["show-ref", "--verify", "--quiet", "refs/heads/" + ref], ["cat-file", "-e", sha + "^{commit}"]):
        subprocess.run(["git", *args], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return sha


def test_count(output: str) -> int:
    """Recognize English VSTest summaries, rejecting zero/all-skipped runs."""
    if re.search(r"No test (?:is available|matches)|No tests? (?:found|discovered)|Failed!|Total(?: tests)?:\s*0\b", output, re.I):
        return 0
    summaries = re.findall(r"^Passed!\s*-\s*Failed:\s*0,\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)", output, re.M)
    if summaries:
        return sum(int(p) for p, _, _ in summaries) if all(
            int(p) > 0 and int(p) + int(s) == int(t) for p, s, t in summaries) else 0
    totals = re.findall(r"^\s*Total tests:\s*(\d+)\s*$", output, re.M)
    passed = re.findall(r"^\s*Passed:\s*(\d+)\s*$", output, re.M)
    if ("Test Run Successful." in output and "Test Run Failed." not in output
            and totals and len(totals) == len(passed) and all(int(n) > 0 for n in totals + passed)):
        return sum(map(int, passed))
    return 0


def record_violation(prefix: Path, field: str, index: int, reason: str, brief_path: Path | None) -> None:
    # One append per executed violation; repeated claim/diff validation is read-only.
    number = index
    if brief_path is not None:
        locations, current = [], None
        for line_number, line in enumerate(brief_path.read_text(encoding="utf-8").splitlines(), 1):
            match = KEY_RE.match(line)
            if match:
                current, line = match[1], match[2]
            if current == field:
                locations.extend([line_number] * (len(line.split()) if field == "Prebuild"
                                                  else int(line.startswith("$ "))))
        number = locations[index - 1]
    name = brief_path.name if brief_path else prefix.name + ".md"
    log = prefix.parent / "accept-violations.log"
    entry = f"{time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}\t{name}\t{field} line={number}\t{reason}\n"
    fd = os.open(log, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
    try:
        os.chmod(log, 0o600)
        data = entry.encode("utf-8")
        if os.write(fd, data) != len(data):
            raise OSError("violation log append incomplete")
    finally:
        os.close(fd)


def warn_shell(command: str, evidence: Path) -> list:
    # Capture each test before a pipe/redirect can hide its output or its exit code.
    # Other shell commands retain legacy semantics, with pipeline failures enforced.
    directory = shlex.quote(evidence.resolve().as_posix())
    wrapper = f'''_queue_dotnet() {{
  local executable=$1; shift
  if [ "${{1:-}}" != test ]; then command "$executable" "$@"; return $?; fi
  local file rc
  mkdir -p -- {directory} || return 125
  file=$(mktemp {directory}/test.XXXXXXXX) || return 125
  command "$executable" "$@" > "$file" 2>&1; rc=$?
  cat "$file"
  printf '\\nQUEUE_TEST_EXIT=%s\\n' "$rc" >> "$file" || return 125
  return "$rc"
}}
dotnet() {{ _queue_dotnet dotnet "$@"; }}
dotnet.exe() {{ _queue_dotnet dotnet.exe "$@"; }}
export -f _queue_dotnet dotnet dotnet.exe
'''
    # Windows CreateProcess otherwise finds System32's WSL launcher before PATH.
    return [shutil.which("bash") or "bash", "-o", "pipefail", "-c", wrapper + command]


def warn_test_count(command: str, evidence: Path) -> int | None:
    logs = sorted(evidence.glob("test.*"))
    expected = len(re.findall(r"\bdotnet(?:\.exe)?[\"']?\s+[\"']?test\b", command, re.I))
    if len(logs) < expected:
        return 0
    if not logs:
        return 0 if expected else None
    total = 0
    for log in logs:
        output, separator, status = log.read_bytes().rpartition(b"\nQUEUE_TEST_EXIT=")
        if not separator or status != b"0\n":
            return 0
        try:
            count = test_count(output.decode("utf-8", errors="strict"))
        except UnicodeError:
            return 0
        if not count:
            return 0
        total += count
    return total


def run_accept(brief: dict, prefix: Path, base: str = "", templates: list = (),
               policy: str = "enforce", brief_path: Path | None = None) -> int:
    commands = validate(brief, templates, policy)  # Validate the entire list before executing anything.
    if base and not re.fullmatch(r"[0-9a-f]{40,64}", base):
        raise ValueError("Accept base must be a full commit SHA")
    ok = True
    for i, args in enumerate(commands, 1):
        command = args if isinstance(args, str) else None
        evidence_dir = Path(f"{prefix}-accept-{i}-tests")
        if command is not None:
            try:
                accept_argv(command, templates)
            except ValueError as exc:
                record_violation(prefix, "Accept", i, str(exc), brief_path)
            template = "legacy-shell"
            args = warn_shell(command, evidence_dir)
        else:
            template = ("diff-check" if args[0] == "git" else "dotnet-" + args[1] if args[0] == "dotnet"
                        else {"pwsh": "pwsh-script", "python": "python-isolated", "bash": "bash-script",
                              "test": "file-test"}[args[0]])
        if command is None and args[0] == "git":
            args = ["git", "diff", "--no-ext-diff", "--no-textconv", "--check", *([base] if base else [])]
        log = Path(f"{prefix}-accept-{i}.log")
        with log.open("x", encoding="utf-8", newline="\n") as out:
            os.chmod(log, 0o600)
            try:
                # Decode on this thread: Windows subprocess text-reader errors can otherwise
                # lose the decoding exception and return stdout=None.
                result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=3000,
                                        shell=False, env=host_env())
                out.write(result.stdout.decode("utf-8", errors="strict"))
                rc = result.returncode
            except subprocess.TimeoutExpired:
                rc = 124
            except UnicodeError:
                rc = 125
            except OSError:
                rc = 127
        count = (warn_test_count(command, evidence_dir) if command is not None else
                 test_count(log.read_text(encoding="utf-8", errors="replace")) if args[:2] == ["dotnet", "test"] else None)
        evidence = f", tests={count}" if count is not None else ""
        print(f"- Accept {i}: template={template}, exit={rc}{evidence}", flush=True)
        if rc or count == 0:
            ok = False
    return 0 if ok else 1


def run_prebuild(brief: dict, prefix: Path, templates: list = (),
                 policy: str = "enforce", brief_path: Path | None = None,
                 project: str = DEFAULT_PROJECT_PREFIX) -> int:
    validate(brief, templates, policy)
    commands = prebuild_commands(brief, policy, project)
    if not commands:
        return 0
    log = Path(f"{prefix}-prebuild.log")
    with log.open("xb") as out:
        os.chmod(log, 0o600)
        for i, args in enumerate(commands, 1):
            if isinstance(args, str):
                record_violation(prefix, "Prebuild", i,
                                 "Prebuild requires plain project names without paths or options", brief_path)
                args = [shutil.which("bash") or "bash", "-o", "pipefail", "-c", args]
            try:
                result = subprocess.run(args, stdout=out, stderr=subprocess.STDOUT, timeout=3000,
                                        shell=False, env=host_env())
            except subprocess.TimeoutExpired:
                return 124
            except OSError:
                return 127
            if result.returncode:
                return result.returncode
    return 0


def required_free(queue: Path, minimum: str, estimate: str) -> int:
    def positive(value: str) -> int:
        if not re.fullmatch(r"[0-9]+", value) or not 0 < int(value) <= 1000000:
            raise ValueError("disk settings must be positive integers in GiB")
        return int(value)

    # Count every sibling queue conservatively, even if its WT uses a different disk.
    # Only parse this literal setting; never source another queue's shell commands.
    reserved = 0
    for running in queue.parent.glob("*/running"):
        tasks = {path.stem.casefold() for path in running.glob("*.md")}
        pending_log = running.parent / "work/cleanup-pending.log"
        if pending_log.exists():
            paths = set()
            for line in pending_log.read_text(encoding="utf-8").splitlines():
                fields = line.split("\t", 2)
                if len(fields) != 3 or not fields[0] or not fields[1] or not fields[2].startswith("cleanup pending: "):
                    raise ValueError("invalid cleanup-pending.log; manual review required")
                slug, path, _ = fields
                retained = Path(path)
                if retained.exists() and str(retained.resolve()).casefold() not in paths:
                    tasks.add(slug.casefold())
                    paths.add(str(retained.resolve()).casefold())
        count = len(tasks)
        if not count:
            continue
        setting = "20"
        for line in (running.parent / "queue.env").read_text(encoding="utf-8-sig").splitlines():
            if re.match(r"\s*(?:export\s+)?TASK_RESERVE_GB\s*=", line):
                parts = shlex.split(line.split("=", 1)[1], comments=True)
                if len(parts) != 1:
                    raise ValueError("TASK_RESERVE_GB must be a literal positive integer")
                setting = parts[0]
        reserved += count * positive(setting)
    return max(215, positive(minimum), 200 + positive(estimate)) + reserved


def prompt(brief: dict, worktree: str, branch: str, base: str) -> str:
    cmds = accept_commands(brief)
    outside = "\n".join(f"- `{c}`" for c in cmds) or "- (none)"
    parts = [
        f"Goal: {brief.get('Goal', '')}",
        f"Worktree (the only place you write): {worktree} (branch {branch}, from {base}; working tree clean).",
        f"## Scope\n\n{brief.get('Scope', '')}",
        f"## Not in scope\n\n{brief.get('Not in scope', '') or '(nothing else is requested)'}",
        "## Acceptance\n\nRun what your sandbox allows (builds, class-filtered tests, `git diff --check`). "
        f"The host will run these trusted checks after you finish:\n{outside}",
        COMMON,
    ]
    if build_notes():
        parts.append(build_notes())
    parts.append("## Final message (at most 12 lines)\n\nWhat changed (files), the results of what you ran with counts, "
                 "what you deliberately did not do, Open questions (or \"None\").")
    return "\n\n".join(parts) + "\n"


def split(out_file: Path, proposed: Path) -> None:
    text = out_file.read_text(encoding="utf-8")
    report = re.search(r"^=====REPORT=====[ \t]*(?:\n|$)", text, flags=re.MULTILINE)
    blocks = re.split(r"^=====BRIEF (.*?)=====[ \t]*$", text[:report.start()] if report else text,
                      flags=re.MULTILINE)
    batch = list(zip(blocks[1::2], blocks[2::2]))
    if blocks[0].strip() or (not batch and report is None):
        raise ValueError("refill must contain only complete brief blocks")
    queue = proposed.parent
    templates = accept_templates(queue)
    policy = accept_policy(queue)
    risk_rules(queue)  # Invalid host policy must fail before writing any brief/report.
    with claim_lock(queue):
        names = set()
        for slug, body in batch:
            unique_task(queue, slug)
            if slug.casefold() in names:
                raise ValueError("duplicate name in refill batch")
            names.add(slug.casefold())
            try:
                validate(parse(body.strip() + "\n"), templates, policy)
            except ValueError as exc:
                raise ValueError(f"brief {slug}: {exc}") from None
        if report is not None:
            work = queue / "work"
            work.mkdir(exist_ok=True)
            report_path = work / f"{out_file.stem.removesuffix('-out')}-report.md"
            report_path.write_text(text[report.end():], encoding="utf-8", newline="\n")
        for slug, body in batch:
            with (proposed / f"{slug}.md").open("x", encoding="utf-8", newline="\n") as out:
                out.write(body.strip() + "\n")
    count = len(batch)
    print(f"wrote {count} brief(s) to {proposed}")


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    cmd = sys.argv[1]
    if cmd == "quota":
        raise SystemExit(quota_result(sys.stdin.read()))
    if cmd == "unique":
        unique_task(Path(sys.argv[2]), sys.argv[3], Path(sys.argv[4]) if len(sys.argv) > 4 else None)
        return
    if cmd == "required-free":
        print(required_free(Path(sys.argv[2]), sys.argv[3], sys.argv[4]))
        return
    if cmd == "split":
        split(Path(sys.argv[2]), Path(sys.argv[3]))
        return
    brief_path = Path(sys.argv[2])
    brief = parse(brief_path.read_text(encoding="utf-8"))
    if cmd in ("check", "check-diff", "run-accept", "run-prebuild"):
        rules = risk_rules(brief_path.parent.parent)
    if cmd == "get":
        print(brief.get(sys.argv[3], ""))
    elif cmd == "accept":
        print("\n".join(accept_commands(brief)))
    elif cmd == "check":
        validate(brief, accept_templates(brief_path.parent.parent), accept_policy(brief_path.parent.parent))
    elif cmd == "check-diff":
        check_diff(brief, sys.argv[3], accept_templates(brief_path.parent.parent), accept_policy(brief_path.parent.parent), rules)
    elif cmd == "chain-base":
        print(chain_base(brief, Path(sys.argv[3]), sys.argv[4]))
    elif cmd == "run-accept":
        raise SystemExit(run_accept(brief, Path(sys.argv[3]), sys.argv[4] if len(sys.argv) > 4 else "",
                                    accept_templates(brief_path.parent.parent), accept_policy(brief_path.parent.parent), brief_path))
    elif cmd == "run-prebuild":
        raise SystemExit(run_prebuild(brief, Path(sys.argv[3]), accept_templates(brief_path.parent.parent),
                                      accept_policy(brief_path.parent.parent), brief_path,
                                      project_prefix(brief_path.parent.parent)))
    elif cmd == "prompt":
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stdout.write(prompt(brief, sys.argv[3], sys.argv[4], sys.argv[5]))
    else:
        raise SystemExit(f"unknown command {cmd}")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        # Do not echo command text, environment values or file contents on rejection.
        print(str(exc) if isinstance(exc, ValueError) and not isinstance(exc, UnicodeError)
              else "gate input/output or encoding error", file=sys.stderr)
        raise SystemExit(1)
