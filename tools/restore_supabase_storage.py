#!/usr/bin/env python3
"""Restore and verify a private Storage export in an isolated Supabase lab."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import socket
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path, PurePosixPath
from typing import Any


def read_dotenv(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        value = value.strip()
        if (
            len(value) >= 2
            and value[0] == value[-1]
            and value[0] in {"'", '"'}
        ):
            value = value[1:-1]
        values[key.strip()] = value
    return values


def safe_relative_path(name: str) -> PurePosixPath:
    path = PurePosixPath(name)
    if path.is_absolute() or not path.parts or any(
        part in {"", ".", ".."} for part in path.parts
    ):
        raise RuntimeError("Manifest contains an unsafe object path")
    return path


def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def is_missing_object_error(error: urllib.error.HTTPError) -> bool:
    """Handle hosted and self-hosted Storage missing-object responses."""
    if error.code == 404:
        return True
    if error.code != 400:
        return False
    try:
        payload = json.loads(error.read().decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return False
    return payload.get("code") == "NoSuchKey" or payload.get("error") == (
        "not_found"
    )


class LabStorage:
    def __init__(
        self, base_url: str, service_key: str, timeout: int, retries: int
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.retries = retries
        self.headers = {
            "apikey": service_key,
            "Authorization": f"Bearer {service_key}",
            "Connection": "close",
        }

    def object_url(self, bucket: str, name: str) -> str:
        encoded_bucket = urllib.parse.quote(bucket, safe="")
        encoded_name = "/".join(
            urllib.parse.quote(part, safe="")
            for part in safe_relative_path(name).parts
        )
        return (
            f"{self.base_url}/storage/v1/object/"
            f"{encoded_bucket}/{encoded_name}"
        )

    def bucket_exists(self, bucket: str) -> bool:
        request = urllib.request.Request(
            f"{self.base_url}/storage/v1/bucket", headers=self.headers
        )
        with urllib.request.urlopen(request, timeout=self.timeout) as response:
            buckets = json.load(response)
        return any(item.get("id") == bucket for item in buckets)

    def upload(
        self, bucket: str, entry: dict[str, Any], source: Path
    ) -> str:
        expected_hash = str(entry["sha256"])
        try:
            if self.remote_sha256(bucket, str(entry["name"])) == expected_hash:
                return "reused"
            raise RuntimeError(
                "A destination object exists with different bytes"
            )
        except urllib.error.HTTPError as error:
            if not is_missing_object_error(error):
                raise

        metadata = entry.get("metadata")
        content_type = "application/octet-stream"
        if isinstance(metadata, dict):
            content_type = str(
                metadata.get("mimetype")
                or metadata.get("contentType")
                or content_type
            )
        body = source.read_bytes()
        for attempt in range(1, self.retries + 1):
            request = urllib.request.Request(
                self.object_url(bucket, str(entry["name"])),
                data=body,
                headers={
                    **self.headers,
                    "Content-Type": content_type,
                    "x-upsert": "false",
                },
                method="POST",
            )
            try:
                with urllib.request.urlopen(
                    request, timeout=self.timeout
                ) as response:
                    response.read()
                return "uploaded"
            except urllib.error.HTTPError as error:
                if attempt == self.retries:
                    raise
            except (
                urllib.error.URLError,
                TimeoutError,
                socket.timeout,
                OSError,
            ):
                if attempt == self.retries:
                    raise
            time.sleep(min(attempt * 2, 10))
        raise RuntimeError("Upload retry loop ended unexpectedly")

    def remote_sha256(self, bucket: str, name: str) -> str:
        request = urllib.request.Request(
            self.object_url(bucket, name),
            headers=self.headers,
            method="GET",
        )
        digest = hashlib.sha256()
        with urllib.request.urlopen(request, timeout=self.timeout) as response:
            while chunk := response.read(1024 * 1024):
                digest.update(chunk)
        return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Restore an ignored Supabase Storage export into a localhost lab "
            "and verify every object by SHA-256."
        )
    )
    parser.add_argument("--env-file", required=True, type=Path)
    parser.add_argument("--export", required=True, type=Path)
    parser.add_argument("--url", default="http://127.0.0.1:8000")
    parser.add_argument("--service-key", default="SERVICE_ROLE_KEY")
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--retries", type=int, default=3)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    parsed_url = urllib.parse.urlparse(args.url)
    if parsed_url.scheme != "http" or parsed_url.hostname not in {
        "127.0.0.1",
        "localhost",
        "::1",
    }:
        print(
            "Restore refused: the destination must be a localhost HTTP lab.",
            file=sys.stderr,
        )
        return 2
    if args.workers < 1 or args.workers > 16:
        print("Workers must be between 1 and 16.", file=sys.stderr)
        return 2
    if args.retries < 1 or args.retries > 10:
        print("Retries must be between 1 and 10.", file=sys.stderr)
        return 2

    manifest_path = args.export / "manifest.json"
    checksum_path = args.export / "MANIFEST.SHA256"
    objects_root = args.export / "objects"
    if not manifest_path.is_file() or not checksum_path.is_file():
        print("Private export manifest or checksum is missing.", file=sys.stderr)
        return 2

    expected_manifest_hash = checksum_path.read_text(
        encoding="utf-8"
    ).split()[0]
    if sha256_path(manifest_path) != expected_manifest_hash:
        print("Private export manifest checksum failed.", file=sys.stderr)
        return 1

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    bucket = str(manifest.get("bucket") or "")
    entries = manifest.get("objects")
    if not bucket or not isinstance(entries, list):
        print("Private export manifest is invalid.", file=sys.stderr)
        return 1

    for entry in entries:
        relative = safe_relative_path(str(entry.get("name") or ""))
        source = objects_root.joinpath(*relative.parts)
        if (
            not source.is_file()
            or source.stat().st_size != int(entry["size"])
            or sha256_path(source) != str(entry["sha256"])
        ):
            print("A private source object failed verification.", file=sys.stderr)
            return 1

    env = read_dotenv(args.env_file)
    service_key = env.get(args.service_key, "").strip()
    if not service_key:
        print("Local lab service-role credential is missing.", file=sys.stderr)
        return 2

    lab = LabStorage(args.url, service_key, args.timeout, args.retries)
    try:
        if not lab.bucket_exists(bucket):
            print(
                "Restore refused: the target bucket does not exist.",
                file=sys.stderr,
            )
            return 2

        def restore_one(entry: dict[str, Any]) -> str:
            relative = safe_relative_path(str(entry["name"]))
            source = objects_root.joinpath(*relative.parts)
            return lab.upload(bucket, entry, source)

        counts = {"uploaded": 0, "reused": 0}
        with ThreadPoolExecutor(max_workers=args.workers) as executor:
            futures = [executor.submit(restore_one, entry) for entry in entries]
            for completed, future in enumerate(
                as_completed(futures), start=1
            ):
                counts[future.result()] += 1
                if completed == len(entries) or completed % 25 == 0:
                    print(
                        f"Restored {completed}/{len(entries)} objects.",
                        flush=True,
                    )

        mismatches = 0
        with ThreadPoolExecutor(max_workers=args.workers) as executor:
            futures = {
                executor.submit(
                    lab.remote_sha256, bucket, str(entry["name"])
                ): str(entry["sha256"])
                for entry in entries
            }
            for completed, future in enumerate(
                as_completed(futures), start=1
            ):
                if future.result() != futures[future]:
                    mismatches += 1
                if completed == len(entries) or completed % 25 == 0:
                    print(
                        f"Verified {completed}/{len(entries)} restored objects.",
                        flush=True,
                    )

        report = {
            "format": 1,
            "source_manifest_sha256": expected_manifest_hash,
            "bucket": bucket,
            "object_count": len(entries),
            "total_bytes": sum(int(entry["size"]) for entry in entries),
            "uploaded": counts["uploaded"],
            "reused": counts["reused"],
            "hash_mismatches": mismatches,
        }
        report_path = args.export / "local-restore-report.json"
        with tempfile.NamedTemporaryFile(
            "w",
            encoding="utf-8",
            dir=args.export,
            prefix=".restore-report-",
            suffix=".json",
            delete=False,
        ) as temporary:
            json.dump(report, temporary, indent=2, sort_keys=True)
            temporary.write("\n")
            temporary_name = temporary.name
        Path(temporary_name).replace(report_path)

        if mismatches:
            print(
                f"Restore verification failed with {mismatches} mismatches.",
                file=sys.stderr,
            )
            return 1
        print(
            f"Restore verified: {len(entries)} objects, "
            f"{report['total_bytes']} bytes, {counts['reused']} reused.",
            flush=True,
        )
        return 0
    except (urllib.error.URLError, RuntimeError, OSError, ValueError) as error:
        print(f"Storage restore failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
