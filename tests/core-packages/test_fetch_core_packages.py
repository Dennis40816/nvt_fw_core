# Copyright (c) 2026 Dennis Liu. All rights reserved.
"""Exercise the fetch command with temporary files and a loopback HTTP server."""

import copy
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import threading
import unittest


SCRIPT = Path(__file__).resolve().parents[2] / "tools/core-packages/fetch_core_packages.py"
REPOSITORY = "example/core"
RELEASE = "core-v0.1.0"
ASSET = "Nvt.Core.0.1.0.nupkg"
BODY = b"synthetic Core package"


class FetchCorePackagesTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.manifest = self.root / "core-packages.json"
        self.destination = self.root / "artifacts/core-packages"
        self.requests = []
        self.responses = {}
        requests = self.requests
        responses = self.responses

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, format, *args):
                pass

            def do_GET(self):
                requests.append(self.path)
                status, payload, mode = responses.get(self.path, (404, b"not found", "normal"))
                self.send_response(status)
                if mode != "no-length":
                    self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                if mode == "drop":
                    self.wfile.write(payload[:3])
                    self.wfile.flush()
                    self.connection.shutdown(socket.SHUT_RDWR)
                    self.connection.close()
                    self.close_connection = True
                else:
                    self.wfile.write(payload)
                    if mode == "no-length":
                        self.close_connection = True

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(
            target=self.server.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True
        )
        self.thread.start()
        self.addCleanup(self.stop_server)
        self.base = f"http://127.0.0.1:{self.server.server_port}"
        self.package = self.make_package(ASSET, BODY)
        self.write_manifest([self.package])
        self.serve(self.package, BODY)

    def stop_server(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)

    @staticmethod
    def make_package(asset, payload, release=RELEASE):
        return {"release": release, "asset": asset, "sha256": hashlib.sha256(payload).hexdigest()}

    @staticmethod
    def asset_path(package):
        return f"/{REPOSITORY}/releases/download/{package['release']}/{package['asset']}"

    def write_manifest(self, packages):
        value = {"schema": 1, "repository": REPOSITORY, "packages": packages}
        self.manifest.write_text(json.dumps(value), encoding="utf-8")
        return value

    def serve(self, package, payload, status=200, mode="normal"):
        self.responses[self.asset_path(package)] = (status, payload, mode)

    def run_fetch(self, *args, small_limit=None, default_manifest=False):
        command = [sys.executable, "-B"]
        if small_limit is None:
            command.append(str(SCRIPT))
        else:
            # Override only the module constant in this subprocess, without a production option.
            harness = (
                "import importlib.util, sys; "
                f"spec = importlib.util.spec_from_file_location('fetch_core_packages', {str(SCRIPT)!r}); "
                "module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); "
                f"module.MAX_PACKAGE_BYTES = {small_limit}; sys.exit(module.main())"
            )
            command.extend(["-c", harness])
        if not default_manifest:
            command.extend(["--manifest", str(self.manifest)])
        command.extend(["--base-url", self.base, *args])
        environment = os.environ.copy()
        for name in list(environment):
            if name.lower() in {"http_proxy", "https_proxy", "all_proxy"}:
                del environment[name]
        environment["NO_PROXY"] = "127.0.0.1,localhost"
        environment["PYTHONIOENCODING"] = "utf-8"
        return subprocess.run(
            command, cwd=self.root, env=environment, capture_output=True,
            text=True, encoding="utf-8", timeout=15,
        )

    def assert_package_error(self, result, package=None):
        package = package or self.package
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn(package["asset"], result.stderr)
        self.assertIn(package["release"], result.stderr)

    def test_first_download_then_verified_without_request(self):
        first = self.run_fetch(default_manifest=True)
        self.assertEqual(first.returncode, 0, first.stderr)
        self.assertIn("downloaded and verified", first.stdout)
        self.assertEqual((self.destination / ASSET).read_bytes(), BODY)
        self.assertEqual(self.requests, [self.asset_path(self.package)])
        second = self.run_fetch()
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertIn(f"{ASSET} ({RELEASE}): verified", second.stdout)
        self.assertNotIn("downloaded", second.stdout)
        self.assertEqual(len(self.requests), 1)

    def test_two_packages_in_manifest_order_and_final_folder(self):
        other_body = b"synthetic UI package"
        other = self.make_package("Nvt.Core.Avalonia.0.2.0.nupkg", other_body, "core-v0.2.0")
        self.write_manifest([other, self.package])
        self.serve(other, other_body)
        result = self.run_fetch()
        self.assertEqual(result.returncode, 0, result.stderr)
        lines = result.stdout.splitlines()
        self.assertEqual(len(lines), 3)
        self.assertTrue(all(line.startswith("core-packages: ") for line in lines))
        self.assertIn(other["asset"], lines[0])
        self.assertIn(ASSET, lines[1])
        self.assertEqual(lines[2], f"core-packages: {self.destination.resolve()}")
        self.assertEqual(self.requests, [self.asset_path(other), self.asset_path(self.package)])
        self.assertEqual((self.destination / other["asset"]).read_bytes(), other_body)

    def test_independent_fonts_release_download_and_offline_verification(self):
        core_body = b"synthetic Core 0.5.0 package"
        ui_body = b"synthetic Avalonia 0.5.0 package"
        fonts_body = b"synthetic Fonts 0.1.0 package"
        packages = [
            self.make_package("Nvt.Core.0.5.0.nupkg", core_body, "core-v0.5.0"),
            self.make_package("Nvt.Core.Avalonia.0.5.0.nupkg", ui_body, "core-v0.5.0"),
            self.make_package("Nvt.Core.Fonts.0.1.0.nupkg", fonts_body, "core-fonts-v0.1.0"),
        ]
        self.write_manifest(packages)
        for package, payload in zip(packages, (core_body, ui_body, fonts_body)):
            self.serve(package, payload)
        first = self.run_fetch()
        self.assertEqual(first.returncode, 0, first.stderr)
        self.assertEqual(self.requests, [self.asset_path(package) for package in packages])
        for package, payload in zip(packages, (core_body, ui_body, fonts_body)):
            self.assertEqual((self.destination / package["asset"]).read_bytes(), payload)
        offline = self.run_fetch("--offline")
        self.assertEqual(offline.returncode, 0, offline.stderr)
        self.assertEqual(offline.stdout.count(": verified"), 3)
        self.assertEqual(len(self.requests), 3)

    def test_sha256_mismatch_fails_without_retry_or_leftover(self):
        wrong = b"wrong package bytes"
        self.serve(self.package, wrong)
        result = self.run_fetch()
        self.assert_package_error(result)
        self.assertIn("SHA-256 mismatch", result.stderr)
        self.assertIn(f"expected {self.package['sha256']}", result.stderr)
        self.assertIn(f"actual {hashlib.sha256(wrong).hexdigest()}", result.stderr)
        self.assertEqual(len(self.requests), 1)
        self.assertEqual(list(self.destination.iterdir()), [])

    def test_wrong_cached_hash_warns_and_downloads_replacement(self):
        self.destination.mkdir(parents=True)
        target = self.destination / ASSET
        target.write_bytes(b"stale package")
        unrelated = self.destination / "keep.txt"
        unrelated.write_bytes(b"keep this file")
        result = self.run_fetch()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("warning:", result.stderr)
        self.assertIn(ASSET, result.stderr)
        self.assertIn(RELEASE, result.stderr)
        self.assertIn("downloaded and verified", result.stdout)
        self.assertEqual(target.read_bytes(), BODY)
        self.assertEqual(unrelated.read_bytes(), b"keep this file")
        self.assertEqual(len(self.requests), 1)
        self.assertEqual(sorted(path.name for path in self.destination.iterdir()), [ASSET, "keep.txt"])

    def test_failed_replacement_preserves_existing_file(self):
        self.destination.mkdir(parents=True)
        target = self.destination / ASSET
        target.write_bytes(b"stale package")
        self.serve(self.package, b"incorrect replacement")
        result = self.run_fetch()
        self.assert_package_error(result)
        self.assertIn("warning:", result.stderr)
        self.assertEqual(target.read_bytes(), b"stale package")
        self.assertEqual(list(self.destination.iterdir()), [target])
        self.assertEqual(len(self.requests), 1)

    def test_http_404_retries_three_times_then_reports_url(self):
        self.responses.clear()
        result = self.run_fetch()
        self.assert_package_error(result)
        self.assertIn(self.base + self.asset_path(self.package), result.stderr)
        self.assertIn("404", result.stderr)
        self.assertEqual(len(self.requests), 3)
        self.assertEqual(list(self.destination.iterdir()), [])

    def test_dropped_connection_retries_three_times_then_reports_url(self):
        self.serve(self.package, BODY, mode="drop")
        result = self.run_fetch()
        self.assert_package_error(result)
        self.assertIn(self.base + self.asset_path(self.package), result.stderr)
        self.assertRegex(result.stderr, r"download failed: .+: .+")
        self.assertEqual(len(self.requests), 3)
        self.assertEqual(list(self.destination.iterdir()), [])

    def test_offline_verifies_all_files_without_request(self):
        other_body = b"second offline package"
        other = self.make_package("Nvt.Core.Avalonia.0.1.0.nupkg", other_body)
        self.write_manifest([self.package, other])
        self.destination.mkdir(parents=True)
        (self.destination / ASSET).write_bytes(BODY)
        (self.destination / other["asset"]).write_bytes(other_body)
        result = self.run_fetch("--offline")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.count(": verified"), 2)
        self.assertEqual(self.requests, [])

    def test_offline_missing_file_reports_destination_without_request(self):
        other = self.make_package("Nvt.Core.Avalonia.0.1.0.nupkg", b"missing")
        self.write_manifest([self.package, other])
        self.destination.mkdir(parents=True)
        (self.destination / ASSET).write_bytes(BODY)
        result = self.run_fetch("--offline")
        self.assert_package_error(result, other)
        self.assertIn(str(self.destination / other["asset"]), result.stderr)
        self.assertIn("missing", result.stderr)
        self.assertEqual(self.requests, [])

    def test_offline_wrong_hash_reports_destination_without_request(self):
        self.destination.mkdir(parents=True)
        target = self.destination / ASSET
        target.write_bytes(b"wrong")
        result = self.run_fetch("--offline")
        self.assert_package_error(result)
        self.assertIn(str(target), result.stderr)
        self.assertIn(f"expected {self.package['sha256']}", result.stderr)
        self.assertIn(f"actual {hashlib.sha256(b'wrong').hexdigest()}", result.stderr)
        self.assertEqual(target.read_bytes(), b"wrong")
        self.assertEqual(self.requests, [])

    def test_bad_json_is_manifest_error(self):
        self.manifest.write_text("{not JSON", encoding="utf-8")
        result = self.run_fetch()
        self.assertEqual(result.returncode, 2, result.stderr)
        self.assertIn("manifest", result.stderr)
        self.assertEqual(self.requests, [])

    def test_invalid_manifests_fail_before_any_request(self):
        valid = {"schema": 1, "repository": REPOSITORY, "packages": [self.package]}
        cases = []
        for schema in (2, True, "1"):
            value = copy.deepcopy(valid)
            value["schema"] = schema
            cases.append((f"schema {schema!r}", value))
        for repository in ("no-slash", "owner/name/extra", "../core", "owner/name?query"):
            value = copy.deepcopy(valid)
            value["repository"] = repository
            cases.append((f"repository {repository}", value))
        for asset in ("folder/package.nupkg", "Nvt..Core.nupkg", "package.zip", "folder\\package.nupkg"):
            value = copy.deepcopy(valid)
            value["packages"][0]["asset"] = asset
            cases.append((f"asset {asset}", value))
        for release in ("../core-v0.1.0", "core..v0.1.0", "core/v0.1.0"):
            value = copy.deepcopy(valid)
            value["packages"][0]["release"] = release
            cases.append((f"release {release}", value))
        for checksum in (self.package["sha256"].upper(), "a" * 63, "g" * 64):
            value = copy.deepcopy(valid)
            value["packages"][0]["sha256"] = checksum
            cases.append((f"checksum {checksum}", value))
        duplicate = copy.deepcopy(valid)
        duplicate["packages"].append(dict(self.package))
        cases.append(("duplicate asset", duplicate))
        mismatch = copy.deepcopy(valid)
        mismatch["packages"][0]["release"] = "core-v0.2.0"
        cases.append(("Release and asset versions differ", mismatch))
        no_version = copy.deepcopy(valid)
        no_version["packages"][0]["release"] = "latest"
        cases.append(("Release tag without a version", no_version))
        unknown_root = copy.deepcopy(valid)
        unknown_root["extra"] = True
        cases.append(("unknown manifest key", unknown_root))
        unknown_package = copy.deepcopy(valid)
        unknown_package["packages"][0]["extra"] = True
        cases.append(("unknown package key", unknown_package))
        missing_key = copy.deepcopy(valid)
        del missing_key["packages"][0]["sha256"]
        cases.append(("missing package key", missing_key))
        for name, value in cases:
            with self.subTest(name=name):
                self.manifest.write_text(json.dumps(value), encoding="utf-8")
                result = self.run_fetch()
                self.assertEqual(result.returncode, 2, result.stderr)
                self.assertIn("manifest", result.stderr)
                self.assertEqual(self.requests, [])
        self.assertFalse(self.destination.exists())

    def test_relative_destination_resolves_from_manifest_folder(self):
        manifest_folder = self.root / "config"
        manifest_folder.mkdir()
        self.manifest = manifest_folder / "core-packages.json"
        self.write_manifest([self.package])
        result = self.run_fetch("--dest", "downloaded")
        expected = manifest_folder / "downloaded"
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((expected / ASSET).read_bytes(), BODY)
        self.assertEqual(result.stdout.splitlines()[-1], f"core-packages: {expected.resolve()}")
        self.assertFalse((self.root / "downloaded").exists())

    def test_default_destination_resolves_from_manifest_folder(self):
        manifest_folder = self.root / "config"
        manifest_folder.mkdir()
        self.manifest = manifest_folder / "core-packages.json"
        self.write_manifest([self.package])
        result = self.run_fetch()
        expected = manifest_folder / "artifacts/core-packages"
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((expected / ASSET).read_bytes(), BODY)
        self.assertEqual(result.stdout.splitlines()[-1], f"core-packages: {expected.resolve()}")
        self.assertFalse(self.destination.exists())

    def test_invalid_base_url_is_usage_error_without_request(self):
        for base in ("ftp://127.0.0.1", "http://127.0.0.1:bad", "http://127.0.0.1/?query"):
            with self.subTest(base=base):
                result = self.run_fetch("--base-url", base)
                self.assertEqual(result.returncode, 2, result.stderr)
                self.assertEqual(self.requests, [])

    def test_size_limit_with_and_without_content_length(self):
        for mode in ("normal", "no-length"):
            with self.subTest(mode=mode):
                self.requests.clear()
                self.serve(self.package, BODY, mode=mode)
                result = self.run_fetch(small_limit=8)
                self.assert_package_error(result)
                self.assertIn("exceeds 8 byte limit", result.stderr)
                self.assertIn(self.base + self.asset_path(self.package), result.stderr)
                self.assertEqual(len(self.requests), 1)
                self.assertEqual(list(self.destination.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
