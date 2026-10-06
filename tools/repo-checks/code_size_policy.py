# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Measure repository source size and enforce caller-owned hotspot enrollment."""

from __future__ import annotations

import hashlib
import re
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Mapping


EXCLUDED_DIRECTORY_NAMES = frozenset(
    {
        ".git",
        ".pytest_cache",
        ".ruff_cache",
        ".venv",
        "__pycache__",
        "artifacts",
        "bin",
        "obj",
        "release",
    }
)
PYTHON_RUNTIME_EXCLUDED_DIRECTORIES = frozenset(
    {".mypy_cache", ".pytest_cache", ".ruff_cache", ".venv", "__pycache__", "venv"}
)


@dataclass(frozen=True)
class TypeAggregate:
    name: str
    file_count: int
    nonblank_lines: int


@dataclass(frozen=True)
class CodeSizeSnapshot:
    production_files: int
    production_nonblank: int
    duplicate_json_groups: int
    duplicate_json_copies: int
    duplicate_json_nonblank: int
    type_aggregates: tuple[TypeAggregate, ...]
    runtime_production_files: int
    runtime_production_nonblank: int


def is_physical_source_file(
    path: Path,
    root: Path,
    suffixes: frozenset[str],
) -> bool:
    """Return whether a real source file belongs to the measured physical tree."""

    try:
        relative = path.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return (
        path.is_file()
        and path.suffix.casefold() in suffixes
        and not any(
            part.casefold() in EXCLUDED_DIRECTORY_NAMES for part in relative.parts[:-1]
        )
    )


def _matching_files(root: Path, directory: str, suffixes: frozenset[str]) -> list[Path]:
    search_root = root / directory
    if not search_root.is_dir():
        return []
    return sorted(
        path
        for path in search_root.rglob("*")
        if is_physical_source_file(path, root, suffixes)
        and not any(part.casefold() == "generated" for part in path.relative_to(root).parts[:-1])
        and not path.name.casefold().endswith((".g.cs", ".generated.cs"))
    )


def _nonblank_line_count(path: Path) -> int:
    return sum(
        bool(line.strip()) for line in path.read_text(encoding="utf-8-sig").splitlines()
    )


def _worker_runtime_files(root: Path, directory: str | None) -> list[Path]:
    """Measure every owned Python source below the canonical worker package root."""

    if directory is None:
        return []
    search_root = root / directory
    if not search_root.is_dir():
        return []
    resolved_root = search_root.resolve()
    files: list[Path] = []
    for path in search_root.rglob("*"):
        try:
            relative = path.resolve().relative_to(resolved_root)
        except ValueError:
            continue
        if (
            path.is_file()
            and path.suffix.casefold() == ".py"
            and not any(
                part.casefold() in PYTHON_RUNTIME_EXCLUDED_DIRECTORIES
                for part in relative.parts[:-1]
            )
        ):
            files.append(path)
    return sorted(files)


def _runtime_production_files(
    root: Path, excluded_project: str | None, python_directory: str | None
) -> list[Path]:
    """Return the non-UI/runtime source measurement set."""

    csharp_files = [
        path
        for path in _matching_files(root, "src", frozenset({".cs"}))
        if path.relative_to(root).parts[1:2] != (excluded_project,)
    ]
    worker_files = _worker_runtime_files(root, python_directory)
    return [*csharp_files, *worker_files]


# Mask comments and literals before recognizing declarations. Counting still
# uses the original whole file, including nonblank comments and literal lines.
CSHARP_NONCODE = re.compile(
    r'//[^\n]*|/\*.*?\*/|(?P<raw>"{3,}).*?(?P=raw)|'
    r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',
    re.DOTALL,
)
CSHARP_TOKEN = re.compile(r"@?[A-Za-z_]\w*|[{};.<>,():]")


def _delegate_name_index(tokens: list[str], declaration: int) -> int | None:
    """Skip the complete return type, including tuples inside generic types."""
    return_type = declaration + 1
    while return_type < len(tokens) and tokens[return_type] in {"ref", "readonly"}:
        return_type += 1
    angle = parentheses = 0
    for index in range(return_type, len(tokens)):
        token = tokens[index]
        if token in {";", "{", "}"}:
            return None
        if token == "(" and angle == 0 and parentheses == 0:
            name = index - 1
            if tokens[name] == ">":
                depth = 1
                name -= 1
                while name > declaration and depth:
                    depth += (tokens[name] == ">") - (tokens[name] == "<")
                    name -= 1
            if name > return_type and re.fullmatch(r"@?[A-Za-z_]\w*", tokens[name]):
                return name
        angle += (token == "<") - (token == ">")
        parentheses += (token == "(") - (token == ")")
    return None


def _declared_types(text: str) -> set[str]:
    """Read qualified declaration identities, including nested/generic types."""
    tokens = CSHARP_TOKEN.findall(CSHARP_NONCODE.sub(" ", text))
    names: set[str] = set()
    scopes: list[str | None] = []
    file_namespace = ""
    pending: str | None = None
    index = 0
    while index < len(tokens):
        token = tokens[index]
        if token == "namespace":
            end = index + 1
            while end < len(tokens) and tokens[end] not in ("{", ";"):
                end += 1
            name = "".join(tokens[index + 1:end]).replace("@", "")
            if end < len(tokens) and tokens[end] == ";":
                file_namespace = name
            else:
                scopes.append(name)
            index = end + 1
            continue
        if token in {"class", "struct", "record", "interface", "enum", "delegate"}:
            start = index + 1
            if token == "record" and start < len(tokens) and tokens[start] in {"class", "struct"}:
                start += 1
            if token == "delegate":
                start = _delegate_name_index(tokens, index)
                if start is None:
                    index += 1
                    continue
            if start < len(tokens) and re.fullmatch(r"@?[A-Za-z_]\w*", tokens[start]):
                end = start + 1
                name = tokens[start].lstrip("@")
                if end < len(tokens) and tokens[end] == "<":
                    depth, arity = 1, 1
                    end += 1
                    while end < len(tokens) and depth:
                        arity += tokens[end] == "," and depth == 1
                        depth += (tokens[end] == "<") - (tokens[end] == ">")
                        end += 1
                    name += f"`{arity}"
                # A constraint such as `where T : class` is not a declaration.
                if end < len(tokens) and tokens[end] in {"{", "(", ":", ";", "where"}:
                    qualified = ".".join(part for part in (file_namespace, *scopes, name) if part)
                    names.add(qualified)
                    pending = None if token == "delegate" else name
                    index = end
                    continue
        if token == "{":
            scopes.append(pending)
            pending = None
        elif token == "}":
            if scopes:
                scopes.pop()
        elif token == ";":
            pending = None
        index += 1
    return names


def measure_code_size(
    root: Path,
    *,
    runtime_excluded_project: str | None = None,
    python_runtime_directory: str | None = None,
    json_directories: tuple[str, ...] = (),
) -> CodeSizeSnapshot:
    """Sum each declaring file once per qualified type, including single files."""
    source_files = _matching_files(root, "src", frozenset({".cs", ".axaml"}))
    counts = {path: _nonblank_line_count(path) for path in source_files}
    runtime = _runtime_production_files(root, runtime_excluded_project, python_runtime_directory)
    declaring: dict[str, list[Path]] = defaultdict(list)
    for path in source_files:
        if path.suffix.casefold() == ".cs":
            for name in _declared_types(path.read_text(encoding="utf-8-sig")):
                declaring[name].append(path)
    by_hash: dict[bytes, list[Path]] = defaultdict(list)
    for directory in json_directories:
        for path in _matching_files(root, directory, frozenset({".json"})):
            by_hash[hashlib.sha256(path.read_bytes()).digest()].append(path)
    duplicates = [paths for paths in by_hash.values() if len(paths) > 1]
    return CodeSizeSnapshot(
        production_files=len(source_files),
        production_nonblank=sum(counts.values()),
        duplicate_json_groups=len(duplicates),
        duplicate_json_copies=sum(len(paths) - 1 for paths in duplicates),
        duplicate_json_nonblank=sum((len(paths) - 1) * _nonblank_line_count(paths[0]) for paths in duplicates),
        type_aggregates=tuple(TypeAggregate(name, len(paths), sum(counts[path] for path in paths))
                              for name, paths in sorted(declaring.items())),
        runtime_production_files=len(runtime),
        runtime_production_nonblank=sum(_nonblank_line_count(path) for path in runtime),
    )


def review_code_size_policy(
    root: Path,
    *,
    policy_reference: str,
    runtime_excluded_project: str | None = None,
    python_runtime_directory: str | None = None,
    json_directories: tuple[str, ...] = (),
) -> list[str]:
    """One advisory block; no allocation, allowance or secondary size gate."""
    snapshot = measure_code_size(
        root,
        runtime_excluded_project=runtime_excluded_project,
        python_runtime_directory=python_runtime_directory,
        json_directories=json_directories,
    )
    return [
        f"code-size advisory: production {snapshot.production_files} files / "
        f"{snapshot.production_nonblank} nonblank lines; runtime "
        f"{snapshot.runtime_production_files} files / {snapshot.runtime_production_nonblank} nonblank lines; "
        f"{len(snapshot.type_aggregates)} C# type aggregates; duplicate JSON "
        f"{snapshot.duplicate_json_groups} groups / {snapshot.duplicate_json_copies} copies / "
        f"{snapshot.duplicate_json_nonblank} nonblank lines. "
        f"Only hotspot enrollment and measured baselines block under {policy_reference}."
    ]


def validate_code_size_policy(
    root: Path, hotspots: Mapping[str, int], *, entry_lines: int, exit_lines: int
) -> list[str]:
    """Require dynamic enrollment and exact current baselines, never allowances."""
    measured = {a.name: a.nonblank_lines for a in measure_code_size(root).type_aggregates}
    errors = []
    for name in sorted(measured.keys() | hotspots.keys()):
        actual = measured.get(name, 0)
        if name not in hotspots:
            if actual >= entry_lines:
                errors.append(f"code-size hotspot {name}: enroll measured baseline {actual} with owner approval in the pull request")
        elif actual < exit_lines:
            errors.append(f"code-size hotspot {name}: remove entry; measured {actual} is below {exit_lines}")
        elif actual > hotspots[name]:
            errors.append(f"code-size hotspot {name}: raise baseline {hotspots[name]} to measured {actual} with owner approval in the pull request")
        elif actual < hotspots[name]:
            errors.append(f"code-size hotspot {name}: lower baseline {hotspots[name]} to measured {actual}; no approval needed for reduction")
    return errors
