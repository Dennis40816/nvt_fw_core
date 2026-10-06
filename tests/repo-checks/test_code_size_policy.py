# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Port the frozen source's synthetic code-size regressions to unittest."""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "repo-checks"))
import code_size_policy as policy


class CodeSizePolicyTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def write(self, path: str, text: str) -> Path:
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")
        return target

    def sized_type(self, lines: int, declaration: str = "class") -> None:
        self.write(
            "src/Product/Hot.cs",
            f"namespace Product;\npublic {declaration} Hot {{\n"
            + "// body\n" * (lines - 3) + "}\n",
        )

    def validate(self, hotspots: dict[str, int]) -> list[str]:
        return policy.validate_code_size_policy(
            self.root, hotspots, entry_lines=2000, exit_lines=1500
        )

    def test_unenrolled_type_at_threshold_requires_owner_approved_enrollment(self) -> None:
        self.sized_type(2000)
        self.assertEqual(
            ["code-size hotspot Product.Hot: enroll measured baseline 2000 "
             "with owner approval in the pull request"],
            self.validate({}),
        )

    def test_baseline_tracks_measurement_and_exit(self) -> None:
        for lines, action in ((2001, "raise"), (1999, "lower"), (1499, "remove")):
            with self.subTest(lines=lines):
                self.sized_type(lines)
                errors = self.validate({"Product.Hot": 2000})
                self.assertEqual(1, len(errors))
                self.assertIn(action, errors[0])
                self.assertEqual(action == "raise", "owner approval" in errors[0])

    def test_enrolled_exact_baseline_is_valid_including_retention_band(self) -> None:
        for lines in (1500, 1750, 1999, 2000, 2200):
            with self.subTest(lines=lines):
                self.sized_type(lines)
                self.assertEqual([], self.validate({"Product.Hot": lines}))

    def test_unenrolled_type_below_entry_threshold_is_advisory(self) -> None:
        for lines in (1499, 1500, 1999):
            with self.subTest(lines=lines):
                self.sized_type(lines)
                self.assertEqual([], self.validate({}))

    def test_removed_type_must_be_removed_from_enrollment(self) -> None:
        self.assertEqual(
            ["code-size hotspot Product.Gone: remove entry; measured 0 is below 1500"],
            self.validate({"Product.Gone": 2000}),
        )

    def test_reentry_requires_enrollment_after_exit(self) -> None:
        self.sized_type(1499)
        self.assertIn("remove", self.validate({"Product.Hot": 2000})[0])
        self.assertEqual([], self.validate({}))
        self.sized_type(2000)
        self.assertIn("enroll", self.validate({})[0])

    def test_single_file_types_are_measured(self) -> None:
        for declaration in (
            "class", "partial class", "struct", "record", "record struct",
            "interface", "enum",
        ):
            with self.subTest(declaration=declaration):
                self.sized_type(2000, declaration)
                aggregate, = policy.measure_code_size(self.root).type_aggregates
                self.assertEqual(("Product.Hot", 1, 2000),
                                 (aggregate.name, aggregate.file_count, aggregate.nonblank_lines))
                self.assertIn("enroll", self.validate({})[0])

    def test_split_and_two_type_files_count_whole_file_once_for_each_type(self) -> None:
        self.write("src/Product/One.cs", "namespace Product;\npartial class A {}\n"
                   "partial class A {}\nclass B {}\n\n")
        self.write("src/Product/Two.cs", "namespace Product;\npartial class A {}\n")
        aggregates = {a.name: (a.file_count, a.nonblank_lines)
                      for a in policy.measure_code_size(self.root).type_aggregates}
        self.assertEqual({"Product.A": (2, 6), "Product.B": (1, 4)}, aggregates)

    def test_namespace_nested_and_generic_identities_do_not_collide(self) -> None:
        self.write("src/Product/One.cs", "namespace First { class Outer { class Item {} } "
                   "class Item {} }\nnamespace Second { class Item<T> {} class Item {} }\n")
        names = {a.name for a in policy.measure_code_size(self.root).type_aggregates}
        self.assertEqual({"First.Outer", "First.Outer.Item", "First.Item",
                          "Second.Item`1", "Second.Item"}, names)

    def test_comments_and_literals_do_not_declare_types(self) -> None:
        self.write("src/Product/One.cs", 'namespace Product;\n'
                   'class Real { string x = "class Fake {}"; }\n'
                   '// class Comment {}\n/* record Another {} */\n')
        self.assertEqual(["Product.Real"],
                         [a.name for a in policy.measure_code_size(self.root).type_aggregates])

    def test_global_types_and_delegates_are_measured(self) -> None:
        self.write("src/Product/One.cs", "class Global {}\npublic delegate void Callback(int x);\n")
        self.assertEqual({"Global", "Callback"},
                         {a.name for a in policy.measure_code_size(self.root).type_aggregates})

    def test_tuple_return_delegate_is_enrolled_by_its_declared_name(self) -> None:
        declarations = (
            ("public delegate (int X, int Y) Callback();", "Product.Callback"),
            ("public delegate System.Func<(int X, int Y)> Callback();", "Product.Callback"),
            ("public delegate System.Func<(T X, T Y)> Callback<T>();", "Product.Callback`1"),
            ("public delegate ref readonly (int X, int Y) Callback();", "Product.Callback"),
            ("public delegate ref readonly (T X, T Y) Callback<T>();", "Product.Callback`1"),
            ("public delegate ref (int X, int Y) Callback();", "Product.Callback"),
            ("public delegate ref readonly int Callback();", "Product.Callback"),
        )
        for declaration, name in declarations:
            with self.subTest(declaration=declaration):
                self.write("src/Product/Delegate.cs", "namespace Product;\n" + declaration
                           + "\n" + "// body\n" * 1998)
                aggregate, = policy.measure_code_size(self.root).type_aggregates
                self.assertEqual((name, 2000), (aggregate.name, aggregate.nonblank_lines))
                self.assertEqual(
                    [f"code-size hotspot {name}: enroll measured baseline 2000 "
                     "with owner approval in the pull request"], self.validate({}),
                )

    def test_anonymous_delegate_is_not_a_type(self) -> None:
        self.write("src/Product/Worker.cs", "namespace Product;\n"
                   "class Worker { System.Action<int> Run = delegate(int x) { }; }\n")
        self.assertEqual(["Product.Worker"],
                         [a.name for a in policy.measure_code_size(self.root).type_aggregates])

    def test_generated_and_build_directories_are_excluded(self) -> None:
        self.sized_type(2000)
        for directory in ("obj", "bin", "generated", "Generated", "artifacts"):
            self.write(f"src/Product/{directory}/Hidden.cs", "namespace Product;\nclass Hidden {}\n")
        self.write("src/Product/Hidden.g.cs", "namespace Product;\nclass Generated {}\n")
        self.write("tests/Outside.cs", "class TestOnly {}\n")
        self.assertEqual(["Product.Hot"],
                         [a.name for a in policy.measure_code_size(self.root).type_aggregates])

    def test_every_other_measurement_has_one_advisory_block(self) -> None:
        self.sized_type(1999)
        self.write("src/Product/View.axaml", "<View/>\n")
        self.write("tools/worker/src/worker.py", "x = 1\n")
        self.write("profiles/a.json", "{}\n")
        self.write("contracts/b.json", "{}\n")
        self.assertEqual(
            ["code-size advisory: production 2 files / 2000 nonblank lines; runtime "
             "2 files / 2000 nonblank lines; 1 C# type aggregates; duplicate JSON "
             "1 groups / 1 copies / 1 nonblank lines. Only hotspot enrollment and "
             "measured baselines block under repository size policy."],
            policy.review_code_size_policy(
                self.root, policy_reference="repository size policy",
                python_runtime_directory="tools/worker/src",
                json_directories=("profiles", "contracts"),
            ),
        )
        self.assertEqual([], self.validate({}))

    def test_repository_enrollment_matches_current_measurement(self) -> None:
        # The source checks its real enrollment map. Core uses a synthetic map.
        self.sized_type(2179)
        self.assertEqual([], self.validate({"Product.Hot": 2179}))


if __name__ == "__main__":
    unittest.main()
