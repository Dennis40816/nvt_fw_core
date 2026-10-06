# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Port metadata regressions without the source repository's inventory policy."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "repo-checks"))
import skill_metadata_validation as metadata_validation


class SkillMetadataValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.path = self.root / "skills" / "openai.yaml"
        self.path.parent.mkdir()

    def write(self, text: str) -> None:
        self.path.write_text(text, encoding="utf-8")

    def valid_text(self, *, prompt: str = "Use $research for this workflow.",
                   description: str = "Exercise the research workflow") -> str:
        return (
            "interface:\n"
            '  display_name: "Research"\n'
            f"  short_description: {json.dumps(description)}\n"
            f"  default_prompt: {json.dumps(prompt)}\n"
        )

    def validate(self, text: str) -> tuple[dict | None, list[str]]:
        self.write(text)
        errors: list[str] = []
        metadata = metadata_validation.parse_skill_metadata(self.path, self.root, errors)
        if metadata is not None:
            metadata_validation.validate_skill_metadata_fields(
                metadata, self.path, self.root, "research", errors
            )
        return metadata, errors

    def structural_error(self, line: int) -> str:
        return f"skill metadata is not valid YAML at {self.path.relative_to(self.root)}:{line}"

    def test_accepts_exact_metadata_and_invocation_value(self) -> None:
        for policy in ("", "policy:\n  allow_implicit_invocation: true\n",
                       "policy:\n  allow_implicit_invocation: false\n"):
            with self.subTest(policy=policy):
                metadata, errors = self.validate(self.valid_text() + policy)
                self.assertEqual([], errors)
                expected_policy = {} if not policy else {
                    "allow_implicit_invocation": "true" in policy
                }
                self.assertEqual(expected_policy, metadata["policy"])

    def test_rejects_default_prompt_for_another_skill(self) -> None:
        _, errors = self.validate(self.valid_text(prompt="Use $other-skill for this workflow."))
        self.assertEqual(
            [f"skill metadata default_prompt must reference $research: {self.path.relative_to(self.root)}"],
            errors,
        )

    def test_rejects_default_prompt_matching_only_a_skill_prefix(self) -> None:
        for suffix in ("-extra", "_extra", "extra", "9"):
            with self.subTest(suffix=suffix):
                _, errors = self.validate(self.valid_text(prompt=f"Use $research{suffix}."))
                self.assertEqual(1, len(errors))
                self.assertIn("default_prompt must reference $research", errors[0])

    def test_rejects_short_description_outside_codex_bounds(self) -> None:
        for description in ("x" * 24, "x" * 65):
            with self.subTest(length=len(description)):
                _, errors = self.validate(self.valid_text(description=description))
                self.assertEqual(
                    ["skill metadata short_description must contain 25 to 64 characters: "
                     f"{self.path.relative_to(self.root)}"], errors,
                )

    def test_rejects_short_description_whose_raw_length_exceeds_bound(self) -> None:
        _, errors = self.validate(self.valid_text(description=f" {'x' * 63} "))
        self.assertEqual(1, len(errors))
        self.assertIn("short_description must contain 25 to 64 characters", errors[0])

    def test_rejects_short_description_whose_trimmed_length_is_below_bound(self) -> None:
        _, errors = self.validate(self.valid_text(description=" " * 25))
        self.assertEqual(1, len(errors))
        self.assertIn("short_description must contain 25 to 64 characters", errors[0])

    def test_rejects_malformed_openai_yaml_before_field_validation(self) -> None:
        metadata, errors = self.validate(self.valid_text() + "malformed: [\n")
        self.assertIsNone(metadata)
        self.assertEqual([self.structural_error(5)], errors)

    def test_rejects_unknown_interface_metadata_field(self) -> None:
        metadata, errors = self.validate(self.valid_text() + '  unexpected: "value"\n')
        self.assertIsNone(metadata)
        self.assertEqual([self.structural_error(5)], errors)

    def test_rejects_unknown_policy_metadata_field(self) -> None:
        metadata, errors = self.validate(self.valid_text()
                                        + "policy:\n  allow_implicit_invocation: false\n"
                                        + "  unexpected_policy: true\n")
        self.assertIsNone(metadata)
        self.assertEqual([self.structural_error(7)], errors)

    def test_closed_schema_rejects_duplicate_and_invalid_entries(self) -> None:
        invalid_lines = (
            '  display_name: "Again"',
            "interface:",
            "unknown:",
            "\tdisplay_name: value",
            ' display_name: "Wrong indent"',
            "# comments are outside this closed schema",
            "  display_name: unquoted",
            "  display_name: 12",
            "  display_name: true",
            "  display_name: null",
            "  display_name: []",
        )
        for invalid in invalid_lines:
            with self.subTest(line=invalid):
                metadata, errors = self.validate(self.valid_text() + invalid + "\n")
                self.assertIsNone(metadata)
                self.assertEqual([self.structural_error(5)], errors)

    def test_policy_requires_lowercase_boolean_and_no_duplicates(self) -> None:
        for value in ('"false"', "False", "TRUE", "0", "null"):
            with self.subTest(value=value):
                metadata, errors = self.validate(self.valid_text()
                                                + f"policy:\n  allow_implicit_invocation: {value}\n")
                self.assertIsNone(metadata)
                self.assertEqual([self.structural_error(6)], errors)
        metadata, errors = self.validate(self.valid_text()
                                        + "policy:\n  allow_implicit_invocation: true\n"
                                        + "  allow_implicit_invocation: false\n")
        self.assertIsNone(metadata)
        self.assertEqual([self.structural_error(7)], errors)

    def test_interface_values_require_json_strings(self) -> None:
        for value in ("unquoted", "12", "true", "null", "[]", "{}", "\"unterminated"):
            with self.subTest(value=value):
                text = self.valid_text().replace('"Research"', value)
                metadata, errors = self.validate(text)
                self.assertIsNone(metadata)
                self.assertEqual([self.structural_error(2)], errors)

    def test_blank_lines_and_json_string_escapes_are_preserved(self) -> None:
        metadata, errors = self.validate("\n" + self.valid_text(prompt="Use $research.\nThen proceed.")
                                        + "   \npolicy:\n\n  allow_implicit_invocation: false\n")
        self.assertEqual([], errors)
        self.assertEqual("Use $research.\nThen proceed.", metadata["interface"]["default_prompt"])

    def test_missing_fields_have_exact_order_and_preserve_existing_errors(self) -> None:
        self.write("")
        errors = ["previous error"]
        metadata = metadata_validation.parse_skill_metadata(self.path, self.root, errors)
        self.assertEqual({"interface": {}, "policy": {}}, metadata)
        metadata_validation.validate_skill_metadata_fields(metadata, self.path, self.root, "research", errors)
        self.assertEqual(
            ["previous error"] + [f"skill metadata requires {field}: {self.path.relative_to(self.root)}"
                                  for field in ("display_name", "short_description", "default_prompt")],
            errors,
        )

    def test_description_boundaries_and_prompt_punctuation_are_accepted(self) -> None:
        for length in (25, 64):
            for suffix in (".", ",", " ", "中", ""):
                with self.subTest(length=length, suffix=suffix):
                    _, errors = self.validate(self.valid_text(description="x" * length,
                                                               prompt=f"Use $research{suffix}"))
                    self.assertEqual([], errors)

    def test_whitespace_fields_keep_source_semantics(self) -> None:
        text = self.valid_text().replace('"Research"', '" "')
        self.assertEqual([], self.validate(text)[1])

    def test_regex_metacharacters_in_skill_name_are_literal(self) -> None:
        self.write(self.valid_text(prompt="Use $research.name!"))
        errors: list[str] = []
        metadata = metadata_validation.parse_skill_metadata(self.path, self.root, errors)
        metadata_validation.validate_skill_metadata_fields(metadata, self.path, self.root, "research.name", errors)
        self.assertEqual([], errors)

    def test_io_errors_propagate(self) -> None:
        with self.assertRaises(FileNotFoundError):
            metadata_validation.parse_skill_metadata(self.path, self.root, [])


if __name__ == "__main__":
    unittest.main()
