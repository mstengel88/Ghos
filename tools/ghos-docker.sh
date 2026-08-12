#!/usr/bin/env bash
set -Eeuo pipefail

script_path="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "${BASH_SOURCE[0]}")"
repo_root="$(cd "$(dirname "$script_path")/.." && pwd)"
printf -v remote_command '%q ' docker "$@"
exec "$repo_root/tools/ghos-ssh.sh" "$remote_command"
