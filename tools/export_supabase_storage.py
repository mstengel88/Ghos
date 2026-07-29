#!/usr/bin/env python3
"""Export a Supabase Storage bucket without exposing credentials in output."""

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
from datetime import datetime, timezone
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


class StorageClient:
    def __init__(self, base_url: str, service_key: str, timeout: int) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.headers = {
            "apikey": service_key,
            "Authorization": f"Bearer {service_key}",
        }

    def request_json(
        self, method: str, path: str, payload: dict[str, Any] | None = None
    ) -> Any:
        data = None
        headers = dict(self.headers)
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(
            f"{self.base_url}{path}",
            data=data,
            headers=headers,
            method=method,
        )
        with urllib.request.urlopen(request, timeout=self.timeout) as response:
            return json.load(response)

    def list_directory(
        self, bucket: str, prefix: str, page_size: int
    ) -> list[dict[str, Any]]:
        bucket_path = urllib.parse.quote(bucket, safe="")
        offset = 0
        results: list[dict[str, Any]] = []
        while True:
            page = self.request_json(
                "POST",
                f"/storage/v1/object/list/{bucket_path}",
                {
                    "prefix": prefix,
                    "limit": page_size,
                    "offset": offset,
                    "sortBy": {"column": "name", "order": "asc"},
                },
            )
            if not isinstance(page, list):
                raise RuntimeError("Storage list response was not an array")
            results.extend(page)
            if len(page) < page_size:
                return results
            offset += len(page)

    def download(
        self,
        bucket: str,
        object_name: str,
        destination: Path,
        retries: int,
    ) -> None:
        encoded_bucket = urllib.parse.quote(bucket, safe="")
        encoded_name = "/".join(
            urllib.parse.quote(part, safe="")
            for part in PurePosixPath(object_name).parts
        )
        destination.parent.mkdir(parents=True, exist_ok=True)
        temporary = destination.with_name(f".{destination.name}.part")
        for attempt in range(1, retries + 1):
            request = urllib.request.Request(
                (
                    f"{self.base_url}/storage/v1/object/"
                    f"{encoded_bucket}/{encoded_name}"
                ),
                headers={**self.headers, "Connection": "close"},
                method="GET",
            )
            try:
                with urllib.request.urlopen(
                    request, timeout=self.timeout
                ) as response:
                    with temporary.open("wb") as output:
                        while chunk := response.read(1024 * 1024):
                            output.write(chunk)
                        output.flush()
                        os.fsync(output.fileno())
                temporary.replace(destination)
                return
            except (
                urllib.error.URLError,
                TimeoutError,
                socket.timeout,
                OSError,
            ):
                temporary.unlink(missing_ok=True)
                if attempt == retries:
                    raise
                time.sleep(min(attempt * 2, 10))


def is_directory(entry: dict[str, Any]) -> bool:
    return entry.get("id") is None and entry.get("metadata") is None


def safe_object_path(name: str) -> PurePosixPath:
    path = PurePosixPath(name)
    if path.is_absolute() or not path.parts or any(
        part in {"", ".", ".."} for part in path.parts
    ):
        raise RuntimeError("Storage returned an unsafe object path")
    return path


def enumerate_objects(
    client: StorageClient, bucket: str, page_size: int, workers: int
) -> list[dict[str, Any]]:
    pending = [""]
    objects: list[dict[str, Any]] = []
    visited: set[str] = set()
    directories_scanned = 0
    with ThreadPoolExecutor(max_workers=workers) as executor:
        while pending:
            batch = [prefix for prefix in pending if prefix not in visited]
            pending = []
            for prefix in batch:
                visited.add(prefix)
            pages = executor.map(
                lambda prefix: client.list_directory(
                    bucket, prefix, page_size
                ),
                batch,
            )
            for prefix, entries in zip(batch, pages):
                directories_scanned += 1
                for entry in entries:
                    child_name = str(entry.get("name") or "")
                    full_name = f"{prefix}/{child_name}".strip("/")
                    safe_object_path(full_name)
                    if is_directory(entry):
                        pending.append(full_name)
                    else:
                        item = dict(entry)
                        item["name"] = full_name
                        objects.append(item)
            print(
                f"Scanned {directories_scanned} private directories; "
                f"found {len(objects)} objects.",
                flush=True,
            )
    objects.sort(key=lambda item: item["name"])
    return objects


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def declared_size(entry: dict[str, Any]) -> int | None:
    metadata = entry.get("metadata")
    if not isinstance(metadata, dict):
        return None
    value = metadata.get("size")
    try:
        return int(value) if value is not None else None
    except (TypeError, ValueError):
        return None


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Download every object in a Supabase Storage bucket and create a "
            "SHA-256 manifest. Secrets and object names are never printed."
        )
    )
    parser.add_argument("--env-file", required=True, type=Path)
    parser.add_argument("--bucket", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--url-key", default="SUPABASE_URL")
    parser.add_argument(
        "--service-key", default="SUPABASE_SERVICE_ROLE_KEY"
    )
    parser.add_argument("--page-size", type=int, default=100)
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--retries", type=int, default=3)
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument(
        "--expected-count",
        type=int,
        help=(
            "Require the private listing to contain this many objects. "
            "A cached listing with another count is refreshed."
        ),
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.env_file.is_file():
        print("Environment file not found.", file=sys.stderr)
        return 2
    if args.page_size < 1 or args.page_size > 1000:
        print("Page size must be between 1 and 1000.", file=sys.stderr)
        return 2
    if args.workers < 1 or args.workers > 16:
        print("Workers must be between 1 and 16.", file=sys.stderr)
        return 2
    if args.retries < 1 or args.retries > 10:
        print("Retries must be between 1 and 10.", file=sys.stderr)
        return 2

    env = read_dotenv(args.env_file)
    base_url = env.get(args.url_key, "").strip()
    service_key = env.get(args.service_key, "").strip()
    if not base_url or not service_key:
        print(
            "The configured URL or service-role credential is missing.",
            file=sys.stderr,
        )
        return 2

    export_root = args.output.resolve()
    objects_root = export_root / "objects"
    export_root.mkdir(parents=True, exist_ok=True)
    try:
        os.chmod(export_root, 0o700)
    except OSError:
        pass

    client = StorageClient(base_url, service_key, args.timeout)
    try:
        source_listing_path = export_root / "source-listing.json"
        refresh_listing = False
        if source_listing_path.is_file():
            source_listing = json.loads(
                source_listing_path.read_text(encoding="utf-8")
            )
            if (
                source_listing.get("bucket") != args.bucket
                or not isinstance(source_listing.get("objects"), list)
            ):
                raise RuntimeError(
                    "The cached private source listing is incompatible"
                )
            objects = source_listing["objects"]
            refresh_listing = (
                args.expected_count is not None
                and len(objects) != args.expected_count
            )
            if refresh_listing:
                print(
                    "Refreshing a cached private listing whose object count "
                    "does not match the expected inventory.",
                    flush=True,
                )
            else:
                print(
                    f"Reusing a private listing of {len(objects)} objects.",
                    flush=True,
                )
        if not source_listing_path.is_file() or refresh_listing:
            objects = enumerate_objects(
                client, args.bucket, args.page_size, args.workers
            )
            source_listing = {
                "format": 1,
                "created_at": datetime.now(timezone.utc).isoformat(),
                "bucket": args.bucket,
                "objects": objects,
            }
            with tempfile.NamedTemporaryFile(
                "w",
                encoding="utf-8",
                dir=export_root,
                prefix=".source-listing-",
                suffix=".json",
                delete=False,
            ) as temporary_listing:
                json.dump(
                    source_listing,
                    temporary_listing,
                    indent=2,
                    sort_keys=True,
                )
                temporary_listing.write("\n")
                temporary_listing_name = temporary_listing.name
            Path(temporary_listing_name).replace(source_listing_path)
        if (
            args.expected_count is not None
            and len(objects) != args.expected_count
        ):
            raise RuntimeError(
                "The private listing contains "
                f"{len(objects)} objects; expected {args.expected_count}. "
                "Reconcile the live Storage inventory before exporting."
            )
        print(
            f"Discovered {len(objects)} objects. Downloading privately...",
            flush=True,
        )

        def download_and_verify(
            entry: dict[str, Any],
        ) -> tuple[dict[str, Any], int]:
            object_name = str(entry["name"])
            relative = safe_object_path(object_name)
            destination = objects_root.joinpath(*relative.parts)
            expected = declared_size(entry)

            reused_file = 0
            if destination.is_file() and (
                expected is None or destination.stat().st_size == expected
            ):
                reused_file = 1
            else:
                client.download(
                    args.bucket,
                    object_name,
                    destination,
                    args.retries,
                )

            actual_size = destination.stat().st_size
            if expected is not None and actual_size != expected:
                raise RuntimeError("A downloaded object has an unexpected size")
            digest = file_sha256(destination)
            return (
                {
                    "name": object_name,
                    "size": actual_size,
                    "sha256": digest,
                    "id": entry.get("id"),
                    "created_at": entry.get("created_at"),
                    "updated_at": entry.get("updated_at"),
                    "last_accessed_at": entry.get("last_accessed_at"),
                    "metadata": entry.get("metadata"),
                },
                reused_file,
            )

        manifest_objects: list[dict[str, Any]] = []
        total_bytes = 0
        reused = 0
        with ThreadPoolExecutor(max_workers=args.workers) as executor:
            futures = {
                executor.submit(download_and_verify, entry): index
                for index, entry in enumerate(objects, start=1)
            }
            for completed, future in enumerate(
                as_completed(futures), start=1
            ):
                manifest_entry, reused_file = future.result()
                manifest_objects.append(manifest_entry)
                total_bytes += int(manifest_entry["size"])
                reused += reused_file
                if completed == len(objects) or completed % 25 == 0:
                    print(
                        f"Verified {completed}/{len(objects)} objects.",
                        flush=True,
                    )
        manifest_objects.sort(key=lambda entry: entry["name"])

        manifest = {
            "format": 1,
            "created_at": datetime.now(timezone.utc).isoformat(),
            "bucket": args.bucket,
            "object_count": len(manifest_objects),
            "total_bytes": total_bytes,
            "objects": manifest_objects,
        }
        with tempfile.NamedTemporaryFile(
            "w",
            encoding="utf-8",
            dir=export_root,
            prefix=".manifest-",
            suffix=".json",
            delete=False,
        ) as temporary_manifest:
            json.dump(manifest, temporary_manifest, indent=2, sort_keys=True)
            temporary_manifest.write("\n")
            temporary_name = temporary_manifest.name
        Path(temporary_name).replace(export_root / "manifest.json")

        manifest_hash = file_sha256(export_root / "manifest.json")
        (export_root / "MANIFEST.SHA256").write_text(
            f"{manifest_hash}  manifest.json\n", encoding="utf-8"
        )
        print(
            f"Export complete: {len(objects)} objects, {total_bytes} bytes, "
            f"{reused} resumed.",
            flush=True,
        )
        print(
            "Object paths remain only in the ignored private export.",
            flush=True,
        )
        return 0
    except (urllib.error.URLError, RuntimeError, OSError, ValueError) as error:
        print(f"Storage export failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
