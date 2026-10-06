# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Pin measurements, caller policy, and diagnostic order on synthetic trees."""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "repo-checks"))
import code_size_policy as policy


class CodeSizeCharacterizationTests(unittest.TestCase):
    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def write(self, path: str, text: str) -> Path:
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")
        return target

    def test_empty_tree_has_exact_snapshot_and_advisory(self) -> None:
        self.assertEqual(policy.CodeSizeSnapshot(0, 0, 0, 0, 0, (), 0, 0),
                         policy.measure_code_size(self.root))
        self.assertEqual(
            ["code-size advisory: production 0 files / 0 nonblank lines; runtime "
             "0 files / 0 nonblank lines; 0 C# type aggregates; duplicate JSON "
             "0 groups / 0 copies / 0 nonblank lines. Only hotspot enrollment and "
             "measured baselines block under size policy."],
            policy.review_code_size_policy(self.root, policy_reference="size policy"),
        )

    def test_caller_paths_control_runtime_and_duplicate_measurements(self) -> None:
        self.write("src/Product/Worker.cs", "namespace Product;\nclass Worker {}\n")
        self.write("src/Product.UI/View.CS", "namespace Product.UI;\nclass View {}\n")
        self.write("src/Product.UI/View.axaml", "<View/>\n")
        self.write("tools/worker/src/worker.py", "# owned\nx = 1\n\n")
        self.write("tools/worker/src/generated/owned.py", "y = 2\n")
        for directory in policy.PYTHON_RUNTIME_EXCLUDED_DIRECTORIES:
            self.write(f"tools/worker/src/{directory.upper()}/hidden.py", "ignored = 1\n")
        self.write("tools/worker/src/notes.txt", "ignored\n")
        self.write("tools/worker/outside.py", "ignored = 1\n")
        self.write("profiles/a.json", "{}\n")
        self.write("contracts/b.JSON", "{}\n")
        self.write("contracts/c.json", "{ }\n")
        self.write("contracts/obj/hidden.json", "{}\n")
        snapshot = policy.measure_code_size(
            self.root, runtime_excluded_project="Product.UI",
            python_runtime_directory="tools/worker/src",
            json_directories=("profiles", "contracts"),
        )
        self.assertEqual(
            policy.CodeSizeSnapshot(
                3, 5, 1, 1, 1,
                (policy.TypeAggregate("Product.UI.View", 1, 2),
                 policy.TypeAggregate("Product.Worker", 1, 2)),
                3, 5,
            ), snapshot,
        )
        default = policy.measure_code_size(self.root)
        self.assertEqual((2, 4, 0), (default.runtime_production_files,
                                   default.runtime_production_nonblank,
                                   default.duplicate_json_groups))

    def test_duplicate_json_uses_bytes_and_counts_each_extra_copy(self) -> None:
        for name in ("a", "b", "c"):
            self.write(f"data/{name}.json", "{\n\n}\n")
        self.write("data/d.json", "{}\n")
        snapshot = policy.measure_code_size(self.root, json_directories=("data",))
        self.assertEqual((1, 2, 4), (snapshot.duplicate_json_groups,
                                    snapshot.duplicate_json_copies,
                                    snapshot.duplicate_json_nonblank))

    def test_bom_blank_lines_comments_and_literals_count_as_in_source(self) -> None:
        self.write("src/Product/One.cs", '\ufeffnamespace Product;\r\n\r\n'
                   'class Real<T> where T : class {\r\n'
                   'string x = """class Hidden {}""";\r\n'
                   'string y = @"record HiddenToo {}";\r\n'
                   "char z = '{'; // class Comment {}\r\n}\r\n   \r\n")
        snapshot = policy.measure_code_size(self.root)
        self.assertEqual((1, 6), (snapshot.production_files, snapshot.production_nonblank))
        self.assertEqual((policy.TypeAggregate("Product.Real`1", 1, 6),),
                         snapshot.type_aggregates)

    def test_all_physical_exclusions_and_generated_suffixes_are_case_insensitive(self) -> None:
        kept = self.write("src/Product/Kept.CS", "class Kept {}\n")
        for directory in policy.EXCLUDED_DIRECTORY_NAMES | {"generated"}:
            self.write(f"src/Product/{directory.upper()}/Hidden.cs", "class Hidden {}\n")
        for suffix in (".g.cs", ".GENERATED.CS"):
            self.write("src/Product/Hidden" + suffix, "class Hidden {}\n")
        snapshot = policy.measure_code_size(self.root)
        self.assertEqual((policy.TypeAggregate("Kept", 1, 1),), snapshot.type_aggregates)
        self.assertTrue(policy.is_physical_source_file(kept, self.root, frozenset({".cs"})))
        self.assertFalse(policy.is_physical_source_file(kept.parent, self.root, frozenset({".cs"})))
        self.assertFalse(policy.is_physical_source_file(kept, self.root, frozenset({".py"})))
        self.assertFalse(policy.is_physical_source_file(kept, self.root / "other", frozenset({".cs"})))

    def test_missing_caller_directories_do_not_change_snapshot(self) -> None:
        self.assertEqual(policy.measure_code_size(self.root), policy.measure_code_size(
            self.root, runtime_excluded_project="Product.UI",
            python_runtime_directory="missing", json_directories=("missing",),
        ))

    def test_caller_thresholds_and_sorted_errors_are_exact(self) -> None:
        self.write("src/Product/B.cs", "namespace Product;\nclass B {}\n// body\n")
        self.write("src/Product/A.cs", "namespace Product;\nclass A {}\n// body\n")
        hotspots = {"Product.Z": 10, "Product.B": 4}
        self.assertEqual(
            ["code-size hotspot Product.A: enroll measured baseline 3 with owner approval in the pull request",
             "code-size hotspot Product.B: lower baseline 4 to measured 3; no approval needed for reduction",
             "code-size hotspot Product.Z: remove entry; measured 0 is below 2"],
            policy.validate_code_size_policy(self.root, hotspots, entry_lines=3, exit_lines=2),
        )
        self.assertEqual({"Product.Z": 10, "Product.B": 4}, hotspots)
        self.assertEqual([], policy.validate_code_size_policy(
            self.root, {}, entry_lines=4, exit_lines=2,
        ))

    def test_retention_boundaries_and_baseline_messages_are_exact(self) -> None:
        self.write("src/Product/Hot.cs", "namespace Product;\nclass Hot {}\n// body\n")
        for baseline, expected in (
            (3, []),
            (2, ["code-size hotspot Product.Hot: raise baseline 2 to measured 3 "
                 "with owner approval in the pull request"]),
            (4, ["code-size hotspot Product.Hot: lower baseline 4 to measured 3; "
                 "no approval needed for reduction"]),
        ):
            with self.subTest(baseline=baseline):
                self.assertEqual(expected, policy.validate_code_size_policy(
                    self.root, {"Product.Hot": baseline}, entry_lines=5, exit_lines=3,
                ))
        self.assertEqual(
            ["code-size hotspot Product.Hot: remove entry; measured 3 is below 4"],
            policy.validate_code_size_policy(self.root, {"Product.Hot": 3},
                                             entry_lines=5, exit_lines=4),
        )

    def test_invalid_source_encoding_propagates(self) -> None:
        target = self.write("src/Product/Broken.cs", "")
        target.write_bytes(b"\xff")
        with self.assertRaises(UnicodeDecodeError):
            policy.measure_code_size(self.root)


if __name__ == "__main__":
    unittest.main()
