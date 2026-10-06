# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Parse the closed YAML schema used by repository skill metadata."""

from __future__ import annotations

import json
import re
from pathlib import Path


ALLOWED_FIELDS = {
    "interface": {"display_name", "short_description", "default_prompt"},
    "policy": {"allow_implicit_invocation"},
}


def parse_skill_metadata(
    metadata_path: Path, repository_root: Path, errors: list[str]
) -> dict[str, dict[str, str | bool]] | None:
    """Return closed skill metadata or append a structural YAML error."""

    metadata: dict[str, dict[str, str | bool]] = {"interface": {}, "policy": {}}
    seen_sections: set[str] = set()
    current_section: str | None = None
    is_valid = True

    for line_number, raw_line in enumerate(
        metadata_path.read_text(encoding="utf-8").splitlines(), start=1
    ):
        if not raw_line.strip():
            continue
        if "\t" in raw_line:
            errors.append(
                f"skill metadata is not valid YAML at {metadata_path.relative_to(repository_root)}:{line_number}"
            )
            is_valid = False
            continue
        section_match = re.fullmatch(r"([a-z][a-z_]*):\s*", raw_line)
        if section_match is not None:
            section = section_match.group(1)
            if section not in metadata or section in seen_sections:
                errors.append(
                    f"skill metadata is not valid YAML at {metadata_path.relative_to(repository_root)}:{line_number}"
                )
                is_valid = False
                current_section = None
                continue
            seen_sections.add(section)
            current_section = section
            continue

        field_match = re.fullmatch(r"  ([a-z][a-z_]*):\s*(.+)", raw_line)
        if field_match is None or current_section is None:
            errors.append(
                f"skill metadata is not valid YAML at {metadata_path.relative_to(repository_root)}:{line_number}"
            )
            is_valid = False
            continue

        field, raw_value = field_match.groups()
        section = metadata[current_section]
        if field not in ALLOWED_FIELDS[current_section] or field in section:
            errors.append(
                f"skill metadata is not valid YAML at {metadata_path.relative_to(repository_root)}:{line_number}"
            )
            is_valid = False
            continue
        if current_section == "interface":
            try:
                value = json.loads(raw_value)
            except json.JSONDecodeError:
                errors.append(
                    f"skill metadata is not valid YAML at {metadata_path.relative_to(repository_root)}:{line_number}"
                )
                is_valid = False
                continue
            if not isinstance(value, str):
                errors.append(
                    f"skill metadata is not valid YAML at {metadata_path.relative_to(repository_root)}:{line_number}"
                )
                is_valid = False
                continue
        elif raw_value in {"true", "false"}:
            value = raw_value == "true"
        else:
            errors.append(
                f"skill metadata is not valid YAML at {metadata_path.relative_to(repository_root)}:{line_number}"
            )
            is_valid = False
            continue
        section[field] = value

    if not is_valid:
        return None
    return metadata


def validate_skill_metadata_fields(
    metadata: dict[str, dict[str, str | bool]],
    metadata_path: Path,
    repository_root: Path,
    skill_name: str,
    errors: list[str],
) -> None:
    """Append semantic metadata errors after successful closed-schema parsing."""

    interface = metadata["interface"]
    for field in ("display_name", "short_description", "default_prompt"):
        value = interface.get(field)
        if not isinstance(value, str) or not value:
            errors.append(
                f"skill metadata requires {field}: {metadata_path.relative_to(repository_root)}"
            )
    short_description = interface.get("short_description")
    if (
        isinstance(short_description, str)
        and not (
            25 <= len(short_description.strip())
            and len(short_description) <= 64
        )
    ):
        errors.append(
            "skill metadata short_description must contain 25 to 64 characters: "
            f"{metadata_path.relative_to(repository_root)}"
        )
    default_prompt = interface.get("default_prompt")
    if (
        isinstance(default_prompt, str)
        and re.search(
            rf"\${re.escape(skill_name)}(?![A-Za-z0-9_-])",
            default_prompt,
        )
        is None
    ):
        errors.append(
            "skill metadata default_prompt must reference "
            f"{'$'}{skill_name}: {metadata_path.relative_to(repository_root)}"
        )
