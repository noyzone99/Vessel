#!/bin/sh
set -eu

publish_dir=$(cd "$(dirname "$1")" && pwd)
library="$publish_dir/$(basename "$1")"
work_dir=$(mktemp -d "${TMPDIR:-/tmp}/vessel-smoke.XXXXXX")
trap 'rm -rf "$work_dir"' EXIT

python3 Tests/Vessel.Native.Tests/smoke.py "$library"
clang -std=c11 -Wall -Wextra -Werror -I "$publish_dir" \
    Tests/Vessel.Native.Tests/smoke.c "$library" "-Wl,-rpath,$publish_dir" \
    -o "$work_dir/smoke"
"$work_dir/smoke" "$work_dir/store"
