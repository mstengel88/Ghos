#!/usr/bin/env bash
set -Eeuo pipefail

source_root="${DUMP_SITE_SOURCE_ROOT:-/Users/mattstengel/Documents/GreenHills APP}"
ios_plist="$source_root/ProjectInfo.plist"
android_gradle="$source_root/GreenHillsINC-Android/app/build.gradle.kts"
expected_base="${DUMP_SITE_EXPECTED_API_BASE:-}"

for source_file in "$ios_plist" "$android_gradle"; do
  if [[ ! -f "$source_file" ]]; then
    echo "Required Dump Site client configuration is missing: $source_file" >&2
    exit 1
  fi
done

if command -v plutil >/dev/null 2>&1; then
  ios_base="$(plutil -extract DumpSiteAPIBaseURL raw "$ios_plist")"
else
  ios_base="$(
    sed -n \
      's#.*<key>DumpSiteAPIBaseURL</key><string>\\([^<]*\\)</string>.*#\\1#p' \
      "$ios_plist" \
      | head -1
  )"
fi

android_base="$(
  awk -F '"' \
    '/DUMP_SITE_API/ { value=$7; gsub(/\\/, "", value); print value; exit }' \
    "$android_gradle"
)"

if [[ -z "$ios_base" || -z "$android_base" ]]; then
  echo "Unable to read both Dump Site API base URLs." >&2
  exit 1
fi
if [[ "$ios_base" != "$android_base" ]]; then
  echo "Dump Site clients disagree about the API base URL." >&2
  echo "iOS:     $ios_base" >&2
  echo "Android: $android_base" >&2
  exit 1
fi
if [[ ! "$ios_base" =~ ^https://[^/]+/.+/dump-site-api$ ]]; then
  echo "Dump Site API base must be HTTPS and end in /dump-site-api." >&2
  exit 1
fi
if [[ "$ios_base" == */ ]]; then
  echo "Dump Site API base must not have a trailing slash." >&2
  exit 1
fi
if [[ -n "$expected_base" && "$ios_base" != "$expected_base" ]]; then
  echo "Dump Site clients do not use the expected candidate API base." >&2
  echo "Expected: $expected_base" >&2
  echo "Actual:   $ios_base" >&2
  exit 1
fi

echo "Dump Site iOS and Android clients use the same valid API base:"
echo "$ios_base"
if [[ "$ios_base" == *".supabase.co/"* ]]; then
  echo "State: managed Supabase (pre-cutover)"
else
  echo "State: custom HTTPS candidate"
fi
