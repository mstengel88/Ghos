#!/usr/bin/env python3
"""Create a secret-safe inventory of local Supabase applications.

The scanner records static API usage and migration objects. It intentionally
does not read or print environment-file values, API keys, JWTs, or passwords.
"""

from __future__ import annotations

import argparse
import json
import re
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path


EXCLUDED_PARTS = {
    ".git",
    ".next",
    ".turbo",
    "bin",
    "build",
    "dist",
    "node_modules",
    "obj",
    "output",
    "tmp",
}
SOURCE_SUFFIXES = {".cs", ".js", ".jsx", ".mjs", ".razor", ".sql", ".ts", ".tsx"}
APP_SOURCE_SUFFIXES = SOURCE_SUFFIXES - {".sql"}

TABLE_CALL = re.compile(r"""\.from\(\s*["']([A-Za-z0-9_ -]+)["']\s*\)""")
RPC_CALL = re.compile(r"""\.rpc\(\s*["']([A-Za-z0-9_ -]+)["']""")
FUNCTION_CALL = re.compile(r"""functions\.invoke\(\s*["']([A-Za-z0-9_ -]+)["']""")
STORAGE_CALL = re.compile(r"""storage\.from\(\s*["']([A-Za-z0-9_ -]+)["']""")
AUTH_CALL = re.compile(r"""auth\.([A-Za-z0-9_]+)""")
PROJECT_ID = re.compile(r"""^\s*project_id\s*=\s*["']([^"']+)["']""", re.MULTILINE)
SUPABASE_HOST = re.compile(r"""https://([a-z0-9-]+)\.supabase\.co""")

CREATE_TABLE = re.compile(
    r"""create\s+table\s+(?:if\s+not\s+exists\s+)?(?:(?P<schema>"?[A-Za-z0-9_]+"?)\.)?(?P<name>"?[A-Za-z0-9_]+"?)""",
    re.IGNORECASE,
)
CREATE_FUNCTION = re.compile(
    r"""create\s+(?:or\s+replace\s+)?function\s+(?:(?P<schema>"?[A-Za-z0-9_]+"?)\.)?(?P<name>"?[A-Za-z0-9_]+"?)""",
    re.IGNORECASE,
)
CREATE_TRIGGER = re.compile(
    r"""create\s+(?:or\s+replace\s+)?trigger\s+(?P<name>"?[A-Za-z0-9_]+"?)""",
    re.IGNORECASE,
)
CREATE_POLICY = re.compile(
    r"""create\s+policy\s+(?P<name>"[^"]+"|[A-Za-z0-9_]+)""",
    re.IGNORECASE,
)
CREATE_BUCKET = re.compile(
    r"""insert\s+into\s+storage\.buckets[\s\S]{0,400}?values\s*\(\s*["']([^"']+)["']""",
    re.IGNORECASE,
)
EXTENSION = re.compile(
    r"""create\s+extension\s+(?:if\s+not\s+exists\s+)?["']?([A-Za-z0-9_-]+)""",
    re.IGNORECASE,
)
KNOWN_AUTH_METHODS = {
    "admin",
    "exchangeCodeForSession",
    "getClaims",
    "getSession",
    "getUser",
    "onAuthStateChange",
    "refreshSession",
    "resetPasswordForEmail",
    "setSession",
    "signInWithIdToken",
    "signInWithOAuth",
    "signInWithPassword",
    "signOut",
    "signUp",
    "updateUser",
}


def is_included(path: Path) -> bool:
    if any(part in EXCLUDED_PARTS for part in path.parts):
        return False

    # Capacitor and similar web wrappers copy compiled Vite bundles into
    # public/assets. Those files are deployment output, not authoritative
    # source, and can retain endpoints from an older build.
    return not any(
        path.parts[index : index + 2] == ("public", "assets")
        for index in range(len(path.parts) - 1)
    )


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return ""


def strip_sql_comments(text: str) -> str:
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    return re.sub(r"--[^\n]*", "", text)


def source_files(root: Path, suffixes: set[str]) -> list[Path]:
    return [
        path
        for path in root.rglob("*")
        if path.is_file() and path.suffix.lower() in suffixes and is_included(path)
    ]


def matches(pattern: re.Pattern[str], texts: list[str]) -> list[str]:
    values: set[str] = set()
    for text in texts:
        for match in pattern.finditer(text):
            values.add(match.group(1))
    return sorted(values)


def qualified_objects(pattern: re.Pattern[str], texts: list[str]) -> list[str]:
    values: set[str] = set()
    for text in texts:
        for match in pattern.finditer(text):
            schema = (match.groupdict().get("schema") or "public").strip('"')
            name = match.group("name").strip('"')
            values.add(f"{schema}.{name}")
    return sorted(values)


def inventory_app(root: Path) -> dict:
    source_paths = source_files(root, APP_SOURCE_SUFFIXES)
    source_texts = [read_text(path) for path in source_paths]

    supabase_root = root / "supabase"
    migration_paths = (
        sorted((supabase_root / "migrations").glob("*.sql"))
        if (supabase_root / "migrations").exists()
        else []
    )
    migration_texts = [strip_sql_comments(read_text(path)) for path in migration_paths]

    function_dirs = (
        sorted(
            path.name
            for path in (supabase_root / "functions").iterdir()
            if path.is_dir() and not path.name.startswith((".", "_"))
        )
        if (supabase_root / "functions").exists()
        else []
    )

    config_path = supabase_root / "config.toml"
    config_text = read_text(config_path) if config_path.exists() else ""
    project_match = PROJECT_ID.search(config_text)

    package_name = None
    package_path = root / "package.json"
    if package_path.exists():
        try:
            package_name = json.loads(read_text(package_path)).get("name")
        except json.JSONDecodeError:
            package_name = None

    realtime_files = 0
    for text in source_texts:
        if any(token in text for token in (".channel(", "postgres_changes")):
            realtime_files += 1

    storage_buckets = matches(STORAGE_CALL, source_texts)
    api_tables = sorted(set(matches(TABLE_CALL, source_texts)) - set(storage_buckets))
    auth_methods = sorted(
        set(matches(AUTH_CALL, source_texts)).intersection(KNOWN_AUTH_METHODS)
    )

    return {
        "path": str(root),
        "package_name": package_name,
        "supabase_project_id": project_match.group(1) if project_match else None,
        "detected_managed_project_refs": [
            value for value in matches(SUPABASE_HOST, source_texts) if len(value) == 20
        ],
        "source_file_count": len(source_paths),
        "migration_count": len(migration_paths),
        "edge_functions": function_dirs,
        "api_tables": api_tables,
        "rpc_calls": matches(RPC_CALL, source_texts),
        "invoked_edge_functions": matches(FUNCTION_CALL, source_texts),
        "storage_buckets_in_code": storage_buckets,
        "auth_methods": auth_methods,
        "realtime_source_file_count": realtime_files,
        "migration_objects": {
            "tables": qualified_objects(CREATE_TABLE, migration_texts),
            "functions": qualified_objects(CREATE_FUNCTION, migration_texts),
            "triggers": sorted(
                {
                    match.group("name").strip('"')
                    for text in migration_texts
                    for match in CREATE_TRIGGER.finditer(text)
                }
            ),
            "policies": sorted(
                {
                    match.group("name").strip('"')
                    for text in migration_texts
                    for match in CREATE_POLICY.finditer(text)
                }
            ),
            "storage_buckets": matches(CREATE_BUCKET, migration_texts),
            "extensions": matches(EXTENSION, migration_texts),
        },
    }


def build_conflicts(apps: list[dict]) -> dict[str, list[str]]:
    owners: dict[str, list[str]] = defaultdict(list)
    for app in apps:
        label = app["label"]
        for table in app["migration_objects"]["tables"]:
            owners[table].append(label)
    return {
        table: sorted(labels)
        for table, labels in sorted(owners.items())
        if len(labels) > 1
    }


def markdown_report(inventory: dict) -> str:
    lines = [
        "# Supabase application inventory",
        "",
        f"Generated: `{inventory['generated_at']}`",
        "",
        "> This is a static, secret-safe inventory. Production row counts, Auth users,",
        "> storage object counts, deployed function versions, and project settings still",
        "> require a read-only production export.",
        "",
        "## Applications",
        "",
        "| Application | Project ID | Migrations | Edge functions | API tables | Auth | Storage | Realtime signals |",
        "|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for app in inventory["applications"]:
        lines.append(
            "| {label} | `{project}` | {migrations} | {functions} | {tables} | {auth} | {storage} | {realtime} |".format(
                label=app["label"],
                project=app["supabase_project_id"] or "not-local",
                migrations=app["migration_count"],
                functions=len(app["edge_functions"]),
                tables=len(app["api_tables"]),
                auth="yes" if app["auth_methods"] else "no",
                storage="yes"
                if app["storage_buckets_in_code"]
                or app["migration_objects"]["storage_buckets"]
                else "no",
                realtime=app["realtime_source_file_count"],
            )
        )

    for app in inventory["applications"]:
        lines.extend(
            [
                "",
                f"## {app['label']}",
                "",
                f"- Path: `{app['path']}`",
                f"- Package: `{app['package_name'] or 'not specified'}`",
                f"- Supabase project ID: `{app['supabase_project_id'] or 'not present locally'}`",
                f"- Managed project refs in source: {', '.join(app['detected_managed_project_refs']) or 'none detected'}",
                f"- Migration files: {app['migration_count']}",
                f"- Edge Functions: {', '.join(app['edge_functions']) or 'none detected'}",
                f"- API tables: {', '.join(app['api_tables']) or 'none detected'}",
                f"- RPC calls: {', '.join(app['rpc_calls']) or 'none detected'}",
                f"- Function invocations: {', '.join(app['invoked_edge_functions']) or 'none detected'}",
                f"- Storage buckets referenced: {', '.join(app['storage_buckets_in_code']) or 'none detected'}",
                f"- Auth methods: {', '.join(app['auth_methods']) or 'none detected'}",
                f"- Migration tables: {', '.join(app['migration_objects']['tables']) or 'none detected'}",
                f"- Database functions: {', '.join(app['migration_objects']['functions']) or 'none detected'}",
                f"- Extensions: {', '.join(app['migration_objects']['extensions']) or 'none detected'}",
            ]
        )

    lines.extend(["", "## Potential table-name collisions", ""])
    if inventory["potential_table_conflicts"]:
        for table, labels in inventory["potential_table_conflicts"].items():
            lines.append(f"- `{table}`: {', '.join(labels)}")
    else:
        lines.append("- None detected from local migration files.")

    lines.extend(
        [
            "",
            "## Production inventory still required",
            "",
            "- Database size, PostgreSQL version, extensions, schemas, row counts, and sequences",
            "- Auth providers, user count, MFA state, redirect URLs, email templates, and SMTP configuration",
            "- Storage buckets, policies, object counts, byte totals, MIME types, and public/private status",
            "- Deployed Edge Function versions, routes, secrets, schedules, and external dependencies",
            "- Database webhooks, cron jobs, Realtime publications, Vault values, and connection consumers",
            "- API keys and JWT migration strategy (values must remain outside source control)",
            "",
        ]
    )
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--json-output", type=Path, required=True)
    parser.add_argument("--markdown-output", type=Path, required=True)
    args = parser.parse_args()

    config = json.loads(args.config.read_text(encoding="utf-8"))
    apps = []
    for entry in config["applications"]:
        result = inventory_app(Path(entry["path"]).expanduser())
        result["label"] = entry["label"]
        result["classification"] = entry.get("classification", "candidate")
        apps.append(result)

    inventory = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "applications": apps,
        "potential_table_conflicts": build_conflicts(apps),
    }

    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    args.markdown_output.parent.mkdir(parents=True, exist_ok=True)
    args.json_output.write_text(json.dumps(inventory, indent=2) + "\n", encoding="utf-8")
    args.markdown_output.write_text(markdown_report(inventory), encoding="utf-8")


if __name__ == "__main__":
    main()
