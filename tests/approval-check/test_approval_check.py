"""Synthetic REST payloads for the approval rule; no GitHub access."""

from __future__ import annotations

import copy
import base64
from contextlib import redirect_stdout
import http.client
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import time
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
FIXTURE = Path(__file__).resolve().parent / "fixtures/nfu/approval"
spec = importlib.util.spec_from_file_location(
    "approval_check", ROOT / "actions/approval-check/approval_check.py")
approval = importlib.util.module_from_spec(spec)
spec.loader.exec_module(approval)
# Model the original NFU layout for the default policy path.
approval.ROOT = FIXTURE
POLICY = json.loads((FIXTURE / ".github/approval-policy.json").read_text(
    encoding="utf-8"))
HEAD = "a" * 40
OLD = "d" * 40
OWNER = {"login": "Dennis40816", "id": 146855708}
OTHER = {"login": "untrusted-reviewer", "id": 999}


class MemoryReader(approval.Reader):
    def __init__(self):
        super().__init__("Dennis40816/nvt-event-buffer-replay", 1, FIXTURE)
        self.paths = []
        self.payloads = {
            label: json.loads((FIXTURE / f"{label}.json").read_text(encoding="utf-8"))
            for label in ("pull", "base-ref", "compare", "files-1", "reviews-1")
        }

    def get(self, label, path, page=None):
        self.paths.append((label, path))
        name = f"{label}-{page}" if page is not None else label
        if name not in self.payloads:
            raise approval.InputError(f"missing fixture {name}")
        return copy.deepcopy(self.payloads[name])


def review(review_id, user=OWNER, state="APPROVED", commit=HEAD, body=""):
    return {
        "id": review_id,
        "user": user,
        "state": state,
        "submitted_at": f"2026-10-01T12:{review_id % 60:02d}:00Z",
        "commit_id": commit,
        "body": body,
    }


class ApprovalCheckTests(unittest.TestCase):
    def setUp(self):
        self.reader = MemoryReader()

    def result(self):
        return {name: (ok, reason) for name, ok, reason in
                approval.evaluate(self.reader, POLICY, None)}

    def owner_gate(self):
        self.reader.payloads["files-1"] = [
            {"filename": "src/View.cs", "status": "modified"}]

    def approve(self, commit=HEAD, review_id=101):
        self.reader.payloads["reviews-1"].append(
            review(review_id, commit=commit))

    def test_review_gated_pass(self):
        result = self.result()
        self.assertTrue(all(ok for ok, _ in result.values()))
        self.assertIn("review-gated", result["gate"][1])

    def test_owner_gated_pass(self):
        self.owner_gate()
        self.approve()
        self.assertTrue(all(ok for ok, _ in self.result().values()))

    def test_owner_gated_without_approval(self):
        self.owner_gate()
        self.assertFalse(self.result()["owner approval"][0])

    def test_approval_on_older_commit(self):
        self.owner_gate()
        self.approve(OLD)
        self.assertFalse(self.result()["owner approval"][0])

    def test_later_changes_requested(self):
        self.owner_gate()
        self.approve()
        self.reader.payloads["reviews-1"].append(
            review(102, state="CHANGES_REQUESTED"))
        self.assertFalse(self.result()["owner approval"][0])

    def test_later_comment_only_review_does_not_cancel_approval(self):
        self.owner_gate()
        self.approve()
        self.reader.payloads["reviews-1"].append(
            review(102, state="COMMENTED"))
        self.assertTrue(self.result()["owner approval"][0])

    def test_dismissed_owner_change_request_blocks_older_approval(self):
        self.owner_gate()
        self.approve()
        self.reader.payloads["reviews-1"].append(
            review(102, state="DISMISSED"))
        self.assertFalse(self.result()["owner approval"][0])

    def test_pending_owner_review_is_ignored(self):
        self.owner_gate()
        self.approve()
        pending = review(103, state="PENDING")
        del pending["submitted_at"]
        self.reader.payloads["reviews-1"].append(pending)
        self.assertTrue(self.result()["owner approval"][0])

    def test_review_record_for_older_head(self):
        self.reader.payloads["reviews-1"][0]["body"] = (
            f"Review record: {OLD} accept\nLimits: synthetic")
        self.assertFalse(self.result()["review record"][0])

    def test_review_record_rejects(self):
        self.reader.payloads["reviews-1"][0]["body"] = (
            f"Review record: {HEAD} reject\nFindings: P1")
        self.assertFalse(self.result()["review record"][0])

    def test_latest_allowed_record_rejects(self):
        self.reader.payloads["reviews-1"].append(
            review(101, state="COMMENTED",
                   body=f"Review record: {HEAD} reject"))
        self.assertFalse(self.result()["review record"][0])

    def test_near_miss_newer_record_blocks_older_accept(self):
        cases = (
            (f"Review record: {HEAD} reject   ", "reject"),
            (f"Review record: `{HEAD}` reject", "malformed"),
            (f"# Review\nReview record: {HEAD} reject", "malformed"),
        )
        for body, reason in cases:
            with self.subTest(body=body):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(
                    review(101, state="COMMENTED", body=body))
                result = {name: (ok, detail) for name, ok, detail in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"][0])
                self.assertIn(reason, result["review record"][1])

    def test_pending_record_is_ignored_before_ordering(self):
        pending = review(101, state="PENDING",
                         body=f"Review record: {HEAD} reject")
        del pending["submitted_at"]
        self.reader.payloads["reviews-1"].append(pending)
        self.assertTrue(self.result()["review record"][0])

    def test_dismissed_or_change_request_record_cannot_accept(self):
        for state in ("DISMISSED", "CHANGES_REQUESTED"):
            with self.subTest(state=state):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(
                    review(101, state=state,
                           body=f"Review record: {HEAD} accept"))
                result = {name: ok for name, ok, _ in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"])

    def test_record_order_uses_submission_time_then_id(self):
        for same_time in (False, True):
            with self.subTest(same_time=same_time):
                reader = MemoryReader()
                later = review(101, state="COMMENTED",
                               body=f"Review record: {HEAD} reject")
                if same_time:
                    later["submitted_at"] = reader.payloads["reviews-1"][0][
                        "submitted_at"]
                reader.payloads["reviews-1"].insert(0, later)
                result = {name: ok for name, ok, _ in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"])

    def test_equal_time_records_order_by_id_in_either_list_order(self):
        for accept_id, reject_id, passes in ((9, 5, True), (5, 9, False)):
            for reverse in (False, True):
                with self.subTest(accept_id=accept_id, reject_id=reject_id,
                                  reverse=reverse):
                    reader = MemoryReader()
                    accept = review(accept_id, user=OWNER, state="COMMENTED",
                                    body=f"Review record: {HEAD} accept")
                    reject = review(reject_id, user=OWNER, state="COMMENTED",
                                    body=f"Review record: {HEAD} reject")
                    reject["submitted_at"] = accept["submitted_at"]
                    ordered = [accept, reject]
                    if reverse:
                        ordered.reverse()
                    reader.payloads["reviews-1"] = ordered
                    result = {name: ok for name, ok, _ in
                              approval.evaluate(reader, POLICY, None)}
                    self.assertEqual(result["review record"], passes)

    def test_any_later_mention_that_is_not_exact_blocks_older_accept(self):
        nbsp, zero_width = chr(0xA0), chr(0x200B)
        for body in (f"**Review record: {HEAD} reject**",
                     f"- Review record: {HEAD} reject",
                     f"## Review record: {HEAD} reject",
                     f"> Review record: {HEAD} reject",
                     f"1. Review record: {HEAD} reject",
                     f"(Review record: {HEAD} reject)",
                     f"| Review record: {HEAD} reject |",
                     f"<b>Review record: {HEAD} reject</b>",
                     f"review record: {HEAD} reject",
                     f"Review Record: {HEAD} reject",
                     f"Updated review record: {HEAD} reject",
                     f"Review  record: {HEAD} reject",
                     f"Review-record: {HEAD} reject",
                     f"Review{nbsp}record: {HEAD} reject",
                     f"{zero_width}Review record: {HEAD} reject",
                     f"Review record:\n{HEAD} reject",
                     "Review record: rejected",
                     "Review record: not accepted",
                     "Review record: declined, see findings",
                     f"Findings first.\nReview record: {HEAD} reject",
                     "Review record read, approving."):
            with self.subTest(body=body):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(
                    review(101, state="COMMENTED", body=body))
                result = {name: (ok, detail) for name, ok, detail in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"][0])
                self.assertIn("malformed", result["review record"][1])

    def test_html_decorated_later_record_blocks_older_accept(self):
        for body in (f"Review <b>record</b>: {HEAD} reject",
                     f"Review&nbsp;record: {HEAD} reject",
                     f"Review&#32;record: {HEAD} reject",
                     f"Review&#x20;record: {HEAD} reject",
                     f"Review<!-- x -->record: {HEAD} reject"):
            with self.subTest(body=body):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(
                    review(101, state="COMMENTED", body=body))
                result = {name: (ok, detail) for name, ok, detail in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"][0])
                self.assertIn("malformed", result["review record"][1])

    def test_invisible_and_markdown_word_splits_block_older_accept(self):
        splits = (
            "Re&shy;view record", "Re&#8203;view record",
            "Re\u00adview record", "Re\u200bview record",
            "Re\u200cview record", "Re\u200dview record",
            "Re\u2060view record", "Re\ufeffview record",
            "Re\u200eview record", "Re\u202eview record",
            "Review re**cord**", "Re_view_ record",
            "Re`view` record", "Re~view~ record",
            "Re<b>view</b> record", "Re<!-- x -->view record",
        )
        for prefix in splits:
            with self.subTest(prefix=prefix):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(review(
                    101, state="COMMENTED",
                    body=f"{prefix}: {HEAD} reject"))
                result = {name: (ok, detail) for name, ok, detail in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"][0])
                self.assertIn("malformed", result["review record"][1])

    def test_normalization_removes_every_format_character(self):
        import unicodedata
        format_chars = "".join(chr(codepoint) for codepoint in
                               range(0x110000)
                               if unicodedata.category(chr(codepoint)) == "Cf")
        self.assertEqual(
            approval.normalized_attempt_text(f"Re{format_chars}view record"),
            "Review record")

    def test_ordinary_prose_keeps_existing_attempt_result(self):
        for body, expected in (("preview recording", False),
                               ("reviewer recorded", True)):
            with self.subTest(body=body):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(review(
                    101, state="COMMENTED", body=body))
                result = {name: ok for name, ok, _ in
                          approval.evaluate(reader, POLICY, None)}
                self.assertEqual(result["review record"], expected)

    def test_comparison_text_is_fast_on_adversarial_bodies(self):
        for pattern in ("for(i<n; i++) ", "<a ", "<!--", "&"):
            with self.subTest(pattern=pattern):
                body = (pattern * (65536 // len(pattern) + 1))[:65536]
                started = time.perf_counter()
                comparison = approval.review_comparison_text(body)
                elapsed = time.perf_counter() - started
                self.assertEqual(comparison, body)
                self.assertLess(elapsed, 2.0, f"{pattern!r}: {elapsed:.3f}s")

    def test_html_parser_cannot_hide_raw_later_record(self):
        for body in (f"<!--\nReview record: {HEAD} reject\n-->",
                     '<a title="review record">x</a>',
                     f"<!--\nReview record: {HEAD} reject",
                     f'<a title="review record: {HEAD} reject'):
            with self.subTest(body=body):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(
                    review(101, state="COMMENTED", body=body))
                result = {name: (ok, detail) for name, ok, detail in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"][0])
                self.assertIn("malformed", result["review record"][1])

    def test_html_without_record_phrase_leaves_older_accept(self):
        for body in ("<b>Looks good</b>",
                     "Review &lt;b&gt;record&lt;/b&gt; was not a record"):
            with self.subTest(body=body):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(
                    review(101, state="COMMENTED", body=body))
                result = {name: ok for name, ok, _ in
                          approval.evaluate(reader, POLICY, None)}
                self.assertTrue(result["review record"])

    def test_later_review_without_the_phrase_leaves_the_record(self):
        for body in ("", "Looks good, approving.", "Reviewed the findings."):
            with self.subTest(body=body):
                reader = MemoryReader()
                reader.payloads["reviews-1"].append(
                    review(101, state="APPROVED", body=body))
                result = {name: ok for name, ok, _ in
                          approval.evaluate(reader, POLICY, None)}
                self.assertTrue(result["review record"])

    def test_exact_first_line_wins_over_later_lines_in_the_same_body(self):
        reader = MemoryReader()
        reader.payloads["reviews-1"].append(review(
            101, state="COMMENTED",
            body=f"\n  \nReview record: {HEAD} accept  \r\n"
                 f"Earlier round: **Review record: {OLD} reject**"))
        result = {name: ok for name, ok, _ in
                  approval.evaluate(reader, POLICY, None)}
        self.assertTrue(result["review record"])

    def test_summary_file_receives_the_result_lines(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            summary = Path(directory) / "summary.md"
            with redirect_stdout(io.StringIO()) as output:
                code = approval.main([
                    "--repository", "Dennis40816/nvt-event-buffer-replay",
                    "--pull-request", "1", "--fixture", str(FIXTURE),
                    "--summary", str(summary)])
            self.assertEqual(code, 0)
            self.assertEqual(summary.read_text(encoding="utf-8"),
                             output.getvalue())

    def test_record_from_unallowed_identity(self):
        self.reader.payloads["reviews-1"][0]["user"] = OTHER
        self.assertFalse(self.result()["review record"][0])

    def test_identity_requires_matching_login_and_id(self):
        for user in ({"login": "not-the-bot", "id": 334370883},
                     {"login": "nfc-agent-dennis40816[bot]", "id": 999}):
            with self.subTest(user=user):
                reader = MemoryReader()
                reader.payloads["reviews-1"][0]["user"] = user
                result = {name: ok for name, ok, _ in
                          approval.evaluate(reader, POLICY, None)}
                self.assertFalse(result["review record"])

    def test_head_behind_base(self):
        self.reader.payloads["compare"]["behind_by"] = 1
        self.assertFalse(self.result()["up to date"][0])

    def test_modified_test_is_owner_gated(self):
        self.reader.payloads["files-1"] = [
            {"filename": "tests/new_case.cs", "status": "modified"}]
        self.assertIn("owner-gated", self.result()["gate"][1])

    def test_added_test_is_review_gated(self):
        self.reader.payloads["files-1"] = [
            {"filename": "tests/new_case.cs", "status": "added"}]
        self.assertIn("review-gated", self.result()["gate"][1])

    def test_renamed_test_file_is_owner_gated(self):
        self.reader.payloads["files-1"] = [
            {"filename": "docs/moved.txt", "previous_filename": "tests/a.txt",
             "status": "renamed"}]
        self.assertIn("owner-gated", self.result()["gate"][1])

    def test_non_added_test_statuses_are_owner_gated(self):
        for status in ("removed", "copied", "changed"):
            with self.subTest(status=status):
                self.reader.payloads["files-1"] = [
                    {"filename": "tests/sample.json", "status": status}]
                self.assertIn("owner-gated", self.result()["gate"][1])

    def test_rename_into_tests_is_owner_gated(self):
        self.reader.payloads["files-1"] = [
            {"filename": "tests/new_name.txt", "previous_filename": "docs/old.txt",
             "status": "renamed"}]
        self.assertIn("owner-gated", self.result()["gate"][1])

    def test_policy_checker_and_workflow_are_owner_gated(self):
        for path in (".github/approval-policy.json", "scripts/approval_check.py",
                     ".github/workflows/approval.yml"):
            with self.subTest(path=path):
                self.reader.payloads["files-1"] = [
                    {"filename": path, "status": "modified"}]
                self.assertIn("owner-gated", self.result()["gate"][1])

    def test_base_branch_main_is_owner_gated(self):
        self.reader.payloads["pull"]["base"]["ref"] = "main"
        self.reader.payloads["base-ref"]["ref"] = "refs/heads/main"
        self.assertIn("owner-gated", self.result()["gate"][1])

    def test_nested_tests_directory_build_props_is_owner_gated(self):
        self.reader.payloads["files-1"] = [
            {"filename": "a/b/tests/Directory.Build.props", "status": "added"}]
        self.assertIn("owner-gated", self.result()["gate"][1])

    def test_differently_cased_nuget_config_is_owner_gated(self):
        self.reader.payloads["files-1"] = [
            {"filename": "build/NuGet.Config", "status": "added"}]
        self.assertIn("owner-gated", self.result()["gate"][1])

    def test_more_than_one_page_of_changed_files(self):
        self.reader.payloads["files-1"] = [
            {"filename": f"docs/item-{index}.md", "status": "added"}
            for index in range(100)]
        self.reader.payloads["files-2"] = [
            {"filename": "nested/scripts/check.py", "status": "added"}]
        self.assertIn("owner-gated", self.result()["gate"][1])

    def test_more_than_thirty_pages_fails_closed(self):
        for page in range(1, 31):
            self.reader.payloads[f"files-{page}"] = [
                {"filename": f"docs/{page}-{index}.md", "status": "added"}
                for index in range(100)]
        with self.assertRaisesRegex(approval.InputError, "exceeds 30 pages"):
            self.reader.files()

    def test_pull_base_sha_old_but_live_tip_is_behind(self):
        self.reader.payloads["pull"]["base"]["sha"] = OLD
        self.reader.payloads["compare"]["behind_by"] = 1
        self.assertFalse(self.result()["up to date"][0])
        self.assertIn(("compare", f"compare/{'b' * 40}...{HEAD}"),
                      self.reader.paths)

    def test_checked_out_base_differs_from_live_tip(self):
        self.reader.payloads["pull"]["base"]["sha"] = OLD
        with self.assertRaises(approval.InputError):
            approval.evaluate(self.reader, POLICY, OLD)

    def test_expected_head_mismatch_fails_all_parts(self):
        output = io.StringIO()
        with redirect_stdout(output):
            code = approval.main([
                "--repository", "Dennis40816/nvt-event-buffer-replay",
                "--pull-request", "1", "--fixture", str(FIXTURE),
                "--expected-head", OLD])
        self.assertEqual(code, 1)
        lines = output.getvalue().splitlines()
        self.assertEqual(len(lines), 4)
        self.assertTrue(all(line.startswith("- FAIL ") and
                            "live head differs from expected head" in line
                            for line in lines))

    def test_expected_head_environment(self):
        args = ["--repository", "Dennis40816/nvt-event-buffer-replay",
                "--pull-request", "1", "--fixture", str(FIXTURE)]
        for value, expected_code in ((OLD, 1), (HEAD, 0), ("", 0)):
            with self.subTest(value=value):
                output = io.StringIO()
                with mock.patch.dict(os.environ,
                                     {"APPROVAL_EXPECTED_HEAD": value}):
                    with redirect_stdout(output):
                        code = approval.main(args)
                self.assertEqual(code, expected_code)
                if value == OLD:
                    self.assertEqual(output.getvalue().count("- FAIL "), 4)
                    self.assertIn("live head differs from expected head",
                                  output.getvalue())
        with mock.patch.dict(os.environ, {}, clear=True):
            with redirect_stdout(io.StringIO()):
                self.assertEqual(approval.main(args), 0)

    def test_invalid_expected_head_environment_fails_closed(self):
        args = ["--repository", "Dennis40816/nvt-event-buffer-replay",
                "--pull-request", "1", "--fixture", str(FIXTURE)]
        for value in ("not-a-sha", "g" * 40):
            with self.subTest(value=value):
                output = io.StringIO()
                with mock.patch.dict(os.environ,
                                     {"APPROVAL_EXPECTED_HEAD": value}):
                    with redirect_stdout(output):
                        code = approval.main(args)
                self.assertEqual(code, 1)
                lines = output.getvalue().splitlines()
                self.assertEqual(len(lines), 4)
                self.assertTrue(all(line.startswith("- FAIL ") and
                                    "expected head SHA is not a full SHA" in line
                                    for line in lines))

    def test_explicit_expected_head_overrides_environment(self):
        args = ["--repository", "Dennis40816/nvt-event-buffer-replay",
                "--pull-request", "1", "--fixture", str(FIXTURE)]
        for environment, explicit, expected_code in (
                (OLD, HEAD, 0), (HEAD, OLD, 1)):
            with self.subTest(environment=environment, explicit=explicit):
                with mock.patch.dict(os.environ,
                                     {"APPROVAL_EXPECTED_HEAD": environment}):
                    with redirect_stdout(io.StringIO()):
                        code = approval.main(args + ["--expected-head", explicit])
                self.assertEqual(code, expected_code)

    def test_closed_or_merged_pull_fails_all_parts(self):
        for merged in (False, True):
            with self.subTest(merged=merged):
                self.reader.payloads["pull"]["state"] = "closed"
                self.reader.payloads["pull"]["merged"] = merged
                output = io.StringIO()
                with mock.patch.object(approval, "Reader", return_value=self.reader):
                    with redirect_stdout(output):
                        code = approval.main([
                            "--repository", "Dennis40816/nvt-event-buffer-replay",
                            "--pull-request", "1", "--fixture", str(FIXTURE)])
                self.assertEqual(code, 1)
                lines = output.getvalue().splitlines()
                self.assertEqual(len(lines), 4)
                self.assertTrue(all(line.startswith("- FAIL ") and
                                    "pull request is not open" in line
                                    for line in lines))

    def test_empty_checked_out_base_fails_closed(self):
        with self.assertRaisesRegex(approval.InputError, "full SHA"):
            approval.evaluate(self.reader, POLICY, "")

    def test_unavailable_live_base_tip_fails_closed(self):
        del self.reader.payloads["base-ref"]
        with self.assertRaises(approval.InputError):
            self.result()

    def test_main_exit_codes_and_exception_mapping(self):
        args = ["--repository", "Dennis40816/nvt-event-buffer-replay",
                "--pull-request", "1", "--fixture", str(FIXTURE)]
        output = io.StringIO()
        with redirect_stdout(output):
            self.assertEqual(approval.main(args), 0)
        self.assertEqual(output.getvalue().count("- PASS "), 4)
        output = io.StringIO()
        with redirect_stdout(output):
            self.assertEqual(approval.main(args + ["--checked-out-base", ""]), 1)
        self.assertEqual(output.getvalue().count("- FAIL "), 4)
        self.assertIn("input unavailable", output.getvalue())
        output = io.StringIO()
        with redirect_stdout(output):
            self.assertEqual(approval.main(args[:4]), 1)
        self.assertIn("--checked-out-base is required", output.getvalue())

    def test_http_protocol_error_becomes_input_error(self):
        reader = approval.Reader("Dennis40816/nvt-event-buffer-replay", 1, None)
        with mock.patch.object(approval.urllib.request, "urlopen",
                               side_effect=http.client.HTTPException("broken")):
            with self.assertRaisesRegex(approval.InputError, "GitHub API"):
                reader.pull()

    def test_codeowners_patterns_equal_policy(self):
        lines = (FIXTURE / "CODEOWNERS").read_text(
            encoding="utf-8").splitlines()
        actual = [line.split() for line in lines if line and not line.startswith("#")]
        expected = [[pattern if pattern.startswith("*.") else f"**/{pattern}",
                     "@Dennis40816"]
                    for pattern in POLICY["owner_gated_patterns"]]
        self.assertEqual(actual, expected)
        self.assertIn("modified-versus-added", "\n".join(lines))

    def test_policy_metadata_matches_contract(self):
        self.assertEqual(POLICY["owner"],
                         {"login": "Dennis40816", "id": 146855708})
        self.assertEqual(POLICY["review_record_authors"], [
            {"login": "Dennis40816", "id": 146855708},
            {"login": "nfc-agent-dennis40816[bot]", "id": 334370883},
        ])
        for principal in [POLICY["owner"], *POLICY["review_record_authors"]]:
            self.assertIsInstance(principal["login"], str)
            self.assertIs(type(principal["id"]), int)
        self.assertIsInstance(POLICY["owner_gated_patterns"], list)
        self.assertEqual(POLICY["tests_non_added"], "tests/**")
        self.assertEqual(POLICY["owner_gated_base_branch"], "main")
        self.assertEqual(POLICY["required_check"], "build-and-test")
        self.assertEqual(POLICY["approval_status_context"],
                         "governance/approval-rule")

    def test_explicit_policy_path_is_used(self):
        import tempfile
        args = ["--repository", "Dennis40816/nvt-event-buffer-replay",
                "--pull-request", "1", "--fixture", str(FIXTURE)]
        changed = copy.deepcopy(POLICY)
        changed["owner_gated_base_branch"] = "0.1.2"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "policy.json"
            path.write_text(json.dumps(changed), encoding="utf-8")
            output = io.StringIO()
            with redirect_stdout(output):
                code = approval.main(args + ["--policy", str(path)])
        self.assertEqual(code, 1)
        self.assertIn("owner-gated: base branch 0.1.2", output.getvalue())
        self.assertIn("- FAIL owner approval:", output.getvalue())

    def test_default_policy_path_is_used(self):
        args = ["--repository", "Dennis40816/nvt-event-buffer-replay",
                "--pull-request", "1", "--fixture", str(FIXTURE)]
        with mock.patch.object(approval, "ROOT", FIXTURE):
            with redirect_stdout(io.StringIO()) as output:
                code = approval.main(args)
        self.assertEqual(code, 0)
        self.assertIn("- PASS gate: review-gated", output.getvalue())
        self.assertEqual(approval.ROOT / ".github/approval-policy.json",
                         FIXTURE / ".github/approval-policy.json")

    def test_action_and_caller_workflow_shape(self):
        action = (ROOT / "actions/approval-check/action.yml").read_text(
            encoding="utf-8")
        caller = (ROOT / "examples/nfu-approval.yml").read_text(
            encoding="utf-8")
        self.assertIn("# GitHub reads this workflow from the pull request's "
                      "merge ref.", caller)
        for permission in ("contents: read", "pull-requests: read",
                           "statuses: write"):
            self.assertRegex(caller, rf"(?m)^  {re.escape(permission)}$")
        for event, required in (
                ("pull_request_review", {"submitted", "edited", "dismissed"}),
                ("pull_request", {"opened", "synchronize", "reopened",
                                  "ready_for_review", "edited",
                                  "converted_to_draft"})):
            match = re.search(
                rf"(?m)^  {event}:\n    types: \[([^\]]+)\]", caller)
            self.assertIsNotNone(match, event)
            actual = {item.strip() for item in match.group(1).split(",")}
            self.assertEqual(actual, required)
        self.assertIn("  cancel-in-progress: true", caller)
        self.assertIn(
            "  group: approval-${{ github.event.pull_request.number }}-"
            "${{ github.event.pull_request.head.sha }}", caller)
        self.assertIn("name: governance / approval", caller)
        self.assertNotIn("continue-on-error", action)
        self.assertIn("default: .github/approval-policy.json", action)
        self.assertIn("default: governance/approval-rule", action)
        self.assertIn("default: ${{ github.token }}", action)
        self.assertEqual(action.count(
            "APPROVAL_STATUS_CONTEXT: ${{ inputs.status-context }}"), 2)
        self.assertIn("APPROVAL_EXPECTED_HEAD: "
                      "${{ github.event.pull_request.head.sha }}", action)
        self.assertIn("APPROVAL_EXIT_CODE: "
                      "${{ steps.approval_check.outputs.exit_code }}", action)
        self.assertIn('echo "exit_code=$result" >> "$GITHUB_OUTPUT"', action)
        self.assertIn('if [ "$APPROVAL_EXIT_CODE" = 0 ]; then', action)
        self.assertIn('if [ "$APPROVAL_EXIT_CODE" != 0 ]; then', action)
        self.assertIn('python -B "$GITHUB_ACTION_PATH/approval_check.py"', action)
        # The policy path is resolved and must stay inside the base checkout.
        self.assertIn('base_root=$(realpath .approval-base)', action)
        self.assertIn('policy_file=$(realpath -e -- '
                      '".approval-base/$APPROVAL_POLICY_PATH")', action)
        self.assertIn('[[ "$policy_file" == "$base_root"/* ]]', action)
        self.assertIn('--policy "$policy_file"', action)
        self.assertNotIn('--policy ".approval-base/', action)
        self.assertIn("result=1\n", action)
        self.assertIn('base_sha=$(git -C .approval-base rev-parse HEAD)', action)
        self.assertIn('echo "APPROVAL_BASE_SHA=$base_sha" >> "$GITHUB_ENV"',
                      action)
        self.assertIn("ref: ${{ github.event.pull_request.base.ref }}", action)
        self.assertIn("path: .approval-base", action)
        self.assertIn("persist-credentials: false", action)
        self.assertIn("for attempt in 1 2 3; do", action)
        self.assertEqual(action.count("for attempt in 1 2 3; do"), 2)
        self.assertEqual(action.count(
            'gh api -X POST "repos/$GH_REPO/statuses/$APPROVAL_HEAD_SHA"'), 2)
        self.assertEqual(action.count('-f "context=$APPROVAL_STATUS_CONTEXT"'),
                         2)
        steps = re.findall(r"(?m)^    - name: (.+)$", action)
        self.assertEqual(steps, [
            "Mark approval pending", "Check out the base branch",
            "Set up Python", "Resolve checked-out base SHA",
            "Check approval", "Report approval status",
            "Fail if approval failed",
        ])
        self.assertEqual(action.count("shell: bash"), 5)
        self.assertEqual(action.count("if: ${{ !cancelled() }}"), 2)
        action_uses = re.findall(r"(?m)^      uses: (.+)$", action)
        # Dependabot bumps these pins, so check the actions and order, not the SHAs.
        self.assertEqual([used.split("@", 1)[0] for used in action_uses],
                         ["actions/checkout", "actions/setup-python"])
        for used in action_uses:
            self.assertRegex(used, r"^[^ @]+@[0-9a-f]{40} # v[0-9.]+$")
        caller_uses = re.findall(r"(?m)^        uses: (.+)$", caller)
        self.assertEqual(caller_uses, [
            "Dennis40816/nvt_fw_core/actions/approval-check@" + "0" * 40])


class CarryoverTests(unittest.TestCase):
    def setUp(self):
        self.reader = MemoryReader()
        self.policy = copy.deepcopy(POLICY)
        self.policy["review_carryover"] = {"enabled": True, "allowlist": ["docs/**"]}
        record = self.reader.payloads["reviews-1"][0]
        record.update(commit_id=OLD, body=f"Review record: {OLD} accept")
        self.reader.payloads["compare"] = {"behind_by": 0, "files": []}
        self.reader.payloads["compare-accepted"] = {"behind_by": 1, "files": []}
        self.reader.payloads["files-1"] = []
        for commit, tree in ((OLD, "1" * 40), (HEAD, "2" * 40), ("b" * 40, "3" * 40)):
            self.reader.payloads[f"commit-{commit}"] = {"tree": {"sha": tree}}
            self.reader.payloads[f"tree-{tree}"] = {"truncated": False, "tree": []}

    def change(self, path="docs/notes.md", before="Original.\n", after="Updated.\n"):
        for text, label, tree in ((before, "compare-accepted", "1" * 40),
                                  (after, "compare", "2" * 40)):
            if text is None:
                continue
            data = text.encode("utf-8")
            blob = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
            self.reader.payloads[f"blob-{blob}"] = {
                "sha": blob, "encoding": "base64",
                "content": base64.b64encode(data).decode() + "\n"}
            files = self.reader.payloads[label]["files"]
            files[:] = [item for item in files if item["filename"] != path]
            files.append({"filename": path, "status": "added" if before is None else "modified",
                          "sha": blob})
            entries = self.reader.payloads[f"tree-{tree}"]["tree"]
            entries[:] = [item for item in entries if item["path"] != path]
            entries.append({"path": path, "mode": "100644", "type": "blob", "sha": blob})
            if label == "compare-accepted":
                self.reader.payloads[f"tree-{'3' * 40}"]["tree"] = copy.deepcopy(entries)
        self.reader.payloads["files-1"] = copy.deepcopy(self.reader.payloads["compare"]["files"])

    def result(self):
        return {name: (ok, reason) for name, ok, reason in
                approval.evaluate(self.reader, self.policy, None)}

    def test_off_keeps_existing_results_and_requests(self):
        self.change()
        for setting in (None, False, {}, {"enabled": False, "allowlist": ["docs/**"]}):
            for current in (False, True):
                with self.subTest(setting=setting, current=current):
                    self.policy.pop("review_carryover", None)
                    if setting is not None:
                        self.policy["review_carryover"] = setting
                    self.reader.payloads["reviews-1"][0]["body"] = (
                        f"Review record: {HEAD if current else OLD} accept")
                    self.reader.paths.clear()
                    self.assertEqual(self.result()["review record"],
                                     (True, "latest allowed review record accepts the current head")
                                     if current else
                                     (False, "latest review record names an older or different head"))
                    self.assertEqual([label for label, _ in self.reader.paths],
                                     ["pull", "base-ref", "compare", "files", "reviews"])

    def test_small_change_carries_and_summary_names_reviewed_sha(self):
        import tempfile
        # Unchanged HTML and URLs do not block a prose change; fences anywhere in the file do.
        unchanged = "<div></div>\nhttps://example.com\n<!-- comment -->\nPlain unchanged text.\n"
        self.change(before=unchanged + "Original.\n", after=unchanged + "Updated.\n")
        self.change("docs/extra.txt", before=None, after="An extra note.\n")
        self.reader.payloads["pull"]["base"]["sha"] = OLD
        with tempfile.TemporaryDirectory() as directory:
            policy = Path(directory) / "policy.json"
            summary = Path(directory) / "summary.md"
            policy.write_text(json.dumps(self.policy), encoding="utf-8")
            with mock.patch.object(approval, "Reader", return_value=self.reader):
                with redirect_stdout(io.StringIO()) as output:
                    code = approval.main([
                        "--repository", "Dennis40816/nvt-event-buffer-replay",
                        "--pull-request", "1", "--fixture", str(FIXTURE),
                        "--policy", str(policy), "--summary", str(summary)])
            self.assertEqual(code, 0)
            self.assertEqual(summary.read_text(encoding="utf-8"), output.getvalue())
        self.assertIn(f"accept carried over from {OLD}; 2 file(s), 3 added/deleted line(s)",
                      output.getvalue())
        for path in ("docs/notes.md", "docs/extra.txt"):
            self.assertIn(path, output.getvalue())
        self.assertIn(("compare-accepted", f"compare/{'b' * 40}...{OLD}"), self.reader.paths)
        # A carried result is not a new review: the next run still compares with OLD.
        self.change(before=unchanged + "Original.\n", after=unchanged + "New.\n" * 40)
        self.assertFalse(self.result()["review record"][0])

    def test_file_and_line_limits(self):
        for counts, passes, reason in (((8,) * 5, True, "40 added/deleted"),
                                       ((1,) * 6, False, "5 files"),
                                       ((41,), False, "40 added/deleted"),
                                       ((9, 8, 8, 8, 8), False, "40 added/deleted")):
            with self.subTest(counts=counts):
                self.setUp()
                for index, count in enumerate(counts):
                    self.change(f"docs/{index}.txt", before=None, after="Note.\n" * count)
                ok, detail = self.result()["review record"]
                self.assertEqual(ok, passes)
                self.assertIn(reason, detail)

    def test_forbidden_content_on_either_side(self):
        cases = (
            ("", "```\n"), ("", "~~~\n"),
            ("```\nold\n```\n", "```\nnew\n```\n"),
            ("~~~\nold\n~~~\n", "~~~\nnew\n~~~\n"),
            ("````\n```\nold\n````\n", "````\n```\nnew\n````\n"),
            ("> ```\n> old\n> ```\n", "> ```\n> new\n> ```\n"),
            ("", "https://example.com\n"), ("", "www.example.com\n"),
            ("", "mailto:review@example.com\n"), ("", "[link](relative.md)\n"),
            ("", "ipfs:content\n"),
            ("", "[link]: relative.md\n"), ("", "<b>hidden</b>\n"),
            ("<script>\nold\n</script>\n", "<script>\nnew\n</script>\n"),
            ("<div>\nold\n</div>\n", "<div>\nnew\n</div>\n"),
            ('<div title="/>\n</div>\n">\nold\n</div>\n',
             '<div title="/>\n</div>\n">\nnew\n</div>\n'),
            ("<!--\nold\n-->\n", "<!--\nnew\n-->\n"),
            ("<a\n title=old\n>\n", "<a\n title=new\n>\n"),
            ("", "Zero\u200bwidth\n"), ("", "Bidi\u202econtrol\n"),
            ("", "Soft\u00adhyphen\n"), ("", "Hidden&#8203;text\n"),
        )
        for before, after in cases:
            for reverse in (False, True):
                with self.subTest(after=after, reverse=reverse):
                    self.setUp()
                    self.change(before=after if reverse else before,
                                after=before if reverse else after)
                    ok, detail = self.result()["review record"]
                    self.assertFalse(ok)
                    self.assertIn("forbidden content", detail)

    def test_indented_or_quoted_fence_does_not_carry(self):
        for fence in ("~~~", "```"):
            for prefix in ("    ", "> ", "    > >\t"):
                before = f"{fence}\n{prefix}{fence}\nold_command\n{fence}\n"
                after = before.replace("old_command", "new_command")
                for reverse in (False, True):
                    with self.subTest(fence=fence, prefix=prefix, reverse=reverse):
                        self.setUp()
                        self.change(before=after if reverse else before,
                                    after=before if reverse else after)
                        ok, detail = self.result()["review record"]
                        self.assertFalse(ok)
                        self.assertIn("forbidden content", detail)

    def test_multiline_link_destinations_do_not_carry(self):
        for opening, closing in (("[download](", ")\n"), ("[download]:", "")):
            for separator in ("\n", "\n\n"):
                before = opening + separator + "old-package.zip\n" + closing
                after = before.replace("old-package.zip", "new-package.zip")
                for reverse in (False, True):
                    with self.subTest(opening=opening, separator=separator, reverse=reverse):
                        self.setUp()
                        self.change(before=after if reverse else before,
                                    after=before if reverse else after)
                        ok, detail = self.result()["review record"]
                        self.assertFalse(ok)
                        self.assertIn("forbidden content", detail)

    def test_fence_in_base_accepted_or_current_file_does_not_carry(self):
        for version in ("base", "accepted", "current", "accepted-and-current"):
            for fence in ("~~~", "```"):
                with self.subTest(version=version, fence=fence):
                    self.setUp()
                    fenced = f"{fence}\nUnchanged command.\n{fence}\n"
                    self.change(
                        before=(fenced if version in ("accepted", "accepted-and-current") else "")
                        + "Original.\n",
                        after=(fenced if version in ("current", "accepted-and-current") else "")
                        + "Updated.\n")
                    if version == "base":
                        data = fenced.encode("utf-8")
                        blob = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
                        self.reader.payloads[f"blob-{blob}"] = {
                            "sha": blob, "encoding": "base64",
                            "content": base64.b64encode(data).decode()}
                        self.reader.payloads[f"tree-{'3' * 40}"]["tree"][0]["sha"] = blob
                    ok, detail = self.result()["review record"]
                    self.assertFalse(ok)
                    self.assertIn("forbidden content", detail)

    def test_unavailable_base_fence_evidence_does_not_carry(self):
        for missing in (f"commit-{'b' * 40}", f"tree-{'3' * 40}"):
            with self.subTest(missing=missing):
                self.setUp()
                self.change()
                del self.reader.payloads[missing]
                self.assertFalse(self.result()["review record"][0])

    def test_clean_base_merge_carries_identical_net_change(self):
        self.change("src/code.cs", before="Reviewed code.\n", after="Reviewed code.\n")
        self.reader.payloads["reviews-1"].append(review(101, commit=OLD))
        result = self.result()
        self.assertTrue(result["review record"][0])
        self.assertIn(f"accept carried over from {OLD}; 0 file(s), 0 added/deleted line(s)",
                      result["review record"][1])
        self.assertFalse(result["owner approval"][0])
        self.assertTrue(result["up to date"][0])
        self.assertFalse(any(label.startswith("blob-") for label, _ in self.reader.paths))

    def test_ineligible_paths_statuses_and_modes(self):
        for path in ("README.md", "docs/release.md", "docs/adr/decision.md", "docs/code.py",
                     "tests/existing.txt"):
            with self.subTest(path=path):
                self.setUp()
                self.policy["review_carryover"]["allowlist"].append("tests/**")
                self.change(path)
                self.assertFalse(self.result()["review record"][0])
        for status in ("removed", "renamed", "copied"):
            for label in ("compare", "compare-accepted"):
                with self.subTest(status=status, label=label):
                    self.setUp()
                    self.change()
                    item = self.reader.payloads[label]["files"][0]
                    item.update(status=status, previous_filename="src/old.cs")
                    self.assertFalse(self.result()["review record"][0])
        for mode, kind in (("100755", "blob"), ("120000", "blob"), ("160000", "commit")):
            with self.subTest(mode=mode):
                self.setUp()
                # Same blob and net metadata: mode changes still require review.
                self.change(before="Same.\n", after="Same.\n")
                self.reader.payloads[f"tree-{'2' * 40}"]["tree"][0].update(mode=mode, type=kind)
                self.assertFalse(self.result()["review record"][0])

    def test_latest_record_and_unavailable_evidence_fail_closed(self):
        for body, state in ((f"Review record: {OLD} reject", "COMMENTED"),
                            (f"**Review record: {OLD} accept**", "COMMENTED"),
                            (f"Review record: {OLD} accept", "DISMISSED"),
                            (f"Review record: {OLD} accept", "CHANGES_REQUESTED")):
            with self.subTest(body=body, state=state):
                self.setUp()
                self.change()
                self.reader.payloads["reviews-1"].append(review(101, state=state, body=body))
                self.assertFalse(self.result()["review record"][0])
                self.assertNotIn("compare-accepted", [label for label, _ in self.reader.paths])
        for label in ("compare", "compare-accepted"):
            for count in (300, 301):
                with self.subTest(label=label, count=count):
                    self.setUp()
                    self.change()
                    self.reader.payloads[label]["files"] *= count
                    self.assertFalse(self.result()["review record"][0])
        for missing in ("compare-accepted", f"commit-{OLD}", f"tree-{'1' * 40}"):
            with self.subTest(missing=missing):
                self.setUp()
                self.change()
                del self.reader.payloads[missing]
                self.assertFalse(self.result()["review record"][0])
        for truncated in ("compare", "compare-accepted", f"tree-{'1' * 40}",
                          f"tree-{'2' * 40}"):
            with self.subTest(truncated=truncated):
                self.setUp()
                self.change()
                self.reader.payloads[truncated]["truncated"] = True
                self.assertFalse(self.result()["review record"][0])
        for content in ("invalid base64!", "/w=="):
            with self.subTest(content=content):
                self.setUp()
                self.change()
                blob = self.reader.payloads["compare"]["files"][0]["sha"]
                self.reader.payloads[f"blob-{blob}"]["content"] = content
                self.assertFalse(self.result()["review record"][0])
        self.setUp()
        self.change(before="Same.\n", after="Same.\n")
        self.reader.payloads[f"tree-{'1' * 40}"]["tree"] = []
        self.assertFalse(self.result()["review record"][0])


if __name__ == "__main__":
    unittest.main()
