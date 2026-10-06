# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Synthetic adoption cases for the NFH two-tier approval policy."""

import copy
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import unittest
from unittest import mock

from test_approval_check import HEAD, OLD, MemoryReader, approval, review


FIXTURE = Path(__file__).resolve().parent / "fixtures/nfh/approval"
POLICY_PATH = FIXTURE / ".github/approval-policy.json"
POLICY = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
OWNER = POLICY["owner"]
REVIEWER = POLICY["review_record_authors"][0]


class NfhApprovalCheckTests(unittest.TestCase):
    def setUp(self):
        self.reader = MemoryReader()
        self.reader.repository = "sample/layout-helper"
        self.reader.fixture = FIXTURE
        self.reader.payloads = {
            label: json.loads((FIXTURE / f"{label}.json").read_text(encoding="utf-8"))
            for label in ("pull", "base-ref", "compare", "files-1", "reviews-1")
        }
        self.policy = copy.deepcopy(POLICY)

    def result(self, checked_out_base=None, expected_head=None):
        return {name: (ok, reason) for name, ok, reason in approval.evaluate(
            self.reader, self.policy, checked_out_base, expected_head)}

    def test_docs_and_modified_tests_need_only_current_head_accept(self):
        for paths in (["docs/guide.md"], ["tests/sample_test.cs"],
                      ["docs/guide.md", "tests/sample_test.cs"]):
            with self.subTest(paths=paths):
                self.reader.payloads["files-1"] = [
                    {"filename": path, "status": "modified"} for path in paths]
                result = self.result()
                self.assertTrue(all(ok for ok, _ in result.values()))
                self.assertIn("review-gated", result["gate"][1])
                self.assertEqual(result["owner approval"], (
                    True, "not required for review-gated pull request"))

    def test_ordinary_tier_has_no_blanket_branch_or_test_status_gate(self):
        for branch in ("1.3.x", "main"):
            for status in ("added", "modified", "removed", "renamed"):
                with self.subTest(branch=branch, status=status):
                    self.reader.payloads["pull"]["base"]["ref"] = branch
                    self.reader.payloads["base-ref"]["ref"] = f"refs/heads/{branch}"
                    self.reader.payloads["files-1"] = [{
                        "filename": "tests/sample_test.cs", "status": status,
                        "previous_filename": "tests/old_test.cs"}]
                    result = self.result()
                    self.assertTrue(all(ok for ok, _ in result.values()))
                    self.assertIn("review-gated", result["gate"][1])

    def test_ordinary_tier_rejects_missing_stale_and_invalid_records(self):
        cases = [[], [review(101, user=REVIEWER, state="COMMENTED")]]
        for body in (f"Review record: {OLD} accept",
                     f"Review record: {HEAD} reject",
                     f"**Review record: {HEAD} accept**"):
            cases.append([review(101, user=REVIEWER, state="COMMENTED", body=body)])
        for reviews in cases:
            with self.subTest(reviews=reviews):
                self.reader.payloads["reviews-1"] = reviews
                self.assertFalse(self.result()["review record"][0])

    def test_ordinary_tier_requires_author_login_and_id(self):
        for user in (OWNER, {"login": REVIEWER["login"], "id": 999},
                     {"login": "untrusted-reviewer", "id": REVIEWER["id"]}):
            with self.subTest(user=user):
                self.reader.payloads["reviews-1"][0]["user"] = user
                self.assertFalse(self.result()["review record"][0])

    def test_owner_approval_alone_does_not_accept_ordinary_tier(self):
        self.reader.payloads["reviews-1"] = [review(101, user=OWNER)]
        self.assertFalse(self.result()["review record"][0])

    def test_high_risk_and_mixed_prs_need_only_owner_approval(self):
        for path in ("src/View.cs", ".github/approval-policy.json", "scripts/build.ps1",
                     ".editorconfig", "Directory.Build.props", "Directory.Packages.props",
                     "global.json", "App.sln", "src/App.csproj", "VERSION",
                     "AGENTS.md", "CONTRIBUTING.md", ".agents/permissions.json",
                     "docs/governance/development-workflow.md"):
            with self.subTest(path=path):
                self.reader.payloads["files-1"] = [
                    {"filename": "docs/guide.md", "status": "modified"},
                    {"filename": path, "status": "modified"}]
                self.reader.payloads["reviews-1"] = [review(101, user=OWNER)]
                result = self.result()
                self.assertTrue(all(ok for ok, _ in result.values()))
                self.assertIn("owner-gated", result["gate"][1])
                self.assertEqual(result["review record"], (
                    True, "not required for owner-gated pull request"))

    def test_owner_tier_does_not_require_a_recommended_review_record(self):
        self.reader.payloads["files-1"] = [
            {"filename": "src/View.cs", "status": "modified"}]
        for body in ("", f"Review record: {OLD} accept",
                     f"Review record: {HEAD} reject", "Review record: malformed"):
            with self.subTest(body=body):
                self.reader.payloads["reviews-1"] = [
                    review(101, user=OWNER),
                    review(102, user=REVIEWER, state="COMMENTED", body=body)]
                self.assertTrue(all(ok for ok, _ in self.result().values()))

    def test_current_head_accept_alone_does_not_accept_owner_tier(self):
        self.reader.payloads["files-1"].append(
            {"filename": "src/View.cs", "status": "modified"})
        result = self.result()
        self.assertFalse(result["owner approval"][0])
        self.assertTrue(result["review record"][0])

    def test_owner_tier_rejects_missing_stale_and_wrong_identity_approval(self):
        self.reader.payloads["files-1"] = [
            {"filename": "src/View.cs", "status": "modified"}]
        cases = [[], [review(101, user=OWNER, commit=OLD)],
                 [review(101, user=REVIEWER)],
                 [review(101, user={"login": OWNER["login"], "id": 999})],
                 [review(101, user={"login": "untrusted-owner", "id": OWNER["id"]})]]
        for reviews in cases:
            with self.subTest(reviews=reviews):
                self.reader.payloads["reviews-1"] = reviews
                self.assertFalse(self.result()["owner approval"][0])

    def test_later_owner_changes_requested_or_dismissed_blocks_approval(self):
        self.reader.payloads["files-1"] = [
            {"filename": "src/View.cs", "status": "modified"}]
        for state in ("CHANGES_REQUESTED", "DISMISSED"):
            with self.subTest(state=state):
                self.reader.payloads["reviews-1"] = [
                    review(101, user=OWNER), review(102, user=OWNER, state=state)]
                self.assertFalse(self.result()["owner approval"][0])

    def test_rename_from_or_into_high_risk_path_is_owner_gated(self):
        for previous, name in (("src/View.cs", "docs/moved.md"),
                               ("docs/guide.md", "src/View.cs")):
            with self.subTest(previous=previous, name=name):
                self.reader.payloads["files-1"] = [{
                    "filename": name, "status": "renamed", "previous_filename": previous}]
                self.assertIn("owner-gated", self.result()["gate"][1])
                self.assertFalse(self.result()["owner approval"][0])

    def test_new_head_invalidates_both_tiers_evidence(self):
        for path, user, body in (
                ("docs/guide.md", REVIEWER, f"Review record: {HEAD} accept"),
                ("src/View.cs", OWNER, "")):
            with self.subTest(path=path):
                self.reader.payloads["pull"]["head"]["sha"] = OLD
                self.reader.payloads["files-1"] = [{"filename": path, "status": "modified"}]
                self.reader.payloads["reviews-1"] = [review(101, user=user, body=body)]
                self.assertFalse(all(ok for ok, _ in self.result().values()))

    def test_freshness_checks_still_apply_to_both_tiers(self):
        for path, user, body in (
                ("docs/guide.md", REVIEWER, f"Review record: {HEAD} accept"),
                ("src/View.cs", OWNER, "")):
            with self.subTest(path=path):
                self.reader.payloads["files-1"] = [{"filename": path, "status": "modified"}]
                self.reader.payloads["reviews-1"] = [review(101, user=user, body=body)]
                self.reader.payloads["compare"]["behind_by"] = 1
                self.assertFalse(self.result()["up to date"][0])
                with self.assertRaisesRegex(approval.InputError, "base moved since checkout"):
                    self.result(checked_out_base=OLD)
                with self.assertRaisesRegex(approval.InputError, "live head differs"):
                    self.result(expected_head=OLD)

    def test_omitted_and_null_legacy_gates_are_unset(self):
        self.policy["owner_gated_base_branch"] = None
        self.policy["tests_non_added"] = None
        self.assertTrue(all(ok for ok, _ in self.result().values()))

    def test_live_head_or_base_move_during_evaluation_fails_both_tiers(self):
        self.reader.fixture = None
        for path, user, body in (
                ("docs/guide.md", REVIEWER, f"Review record: {HEAD} accept"),
                ("src/View.cs", OWNER, "")):
            self.reader.payloads["files-1"] = [{"filename": path, "status": "modified"}]
            self.reader.payloads["reviews-1"] = [review(101, user=user, body=body)]
            initial = self.reader.pull()
            for moved_part in ("head", "base"):
                with self.subTest(path=path, moved_part=moved_part):
                    changed = copy.deepcopy(initial)
                    if moved_part == "head":
                        changed["head"]["sha"] = OLD
                    else:
                        changed["base"]["ref"] = "main"
                    with mock.patch.object(self.reader, "pull", side_effect=[initial, changed]):
                        with self.assertRaisesRegex(approval.InputError, "moved during evaluation"):
                            self.result()
            base = self.reader.payloads["base-ref"]["object"]["sha"]
            with self.subTest(path=path, moved_part="base tip"):
                with mock.patch.object(self.reader, "branch_tip", side_effect=[base, OLD]):
                    with self.assertRaisesRegex(approval.InputError, "moved during evaluation"):
                        self.result()

    def test_only_explicit_false_removes_owner_review_record_requirement(self):
        self.reader.payloads["files-1"] = [
            {"filename": "src/View.cs", "status": "modified"}]
        self.reader.payloads["reviews-1"] = [review(101, user=OWNER)]
        self.policy.pop("owner_requires_review_record")
        self.assertFalse(self.result()["review record"][0])
        for value in (True, None, 0, "false"):
            with self.subTest(value=value):
                self.policy["owner_requires_review_record"] = value
                self.assertFalse(self.result()["review record"][0])

    def test_fixture_cli_uses_nfh_base_policy(self):
        output = io.StringIO()
        with mock.patch.dict("os.environ", {}, clear=True), redirect_stdout(output):
            code = approval.main([
                "--repository", "sample/layout-helper", "--pull-request", "1",
                "--fixture", str(FIXTURE), "--policy", str(POLICY_PATH)])
        self.assertEqual(code, 0)
        self.assertEqual(output.getvalue().count("- PASS "), 4)
        self.assertIn("review-gated", output.getvalue())


if __name__ == "__main__":
    unittest.main()
