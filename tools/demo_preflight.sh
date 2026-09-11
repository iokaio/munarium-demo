#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
demo=${1:?Demo name required}; profile=${2:-default}
case "$demo" in ''|*[!a-z0-9-]*) echo 'Invalid demo name' >&2; exit 2 ;; esac
case "$profile" in default|heldout|stress) ;; *) exit 2 ;; esac
tool_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(dirname "$tool_dir")
test -f "$repo_dir/src/$demo/compose.yaml"
if [ "$profile" != default ]; then test -f "$repo_dir/src/$demo/fixture-profiles.json" || { echo 'This demo has not implemented named fixture profiles yet' >&2; exit 2; }; fi
docker compose version --short
case "$(docker context inspect --format '{{.Endpoints.docker.Host}}')" in unix://*|npipe://*) ;; *) echo 'Select a local Docker context' >&2; exit 2 ;; esac
test "$(docker info --format '{{.OSType}}')" = linux
reports_dir="$repo_dir/artifacts/$demo/preflight"
mkdir -p "$reports_dir"
report_id="$(date -u +%Y%m%dT%H%M%SZ)-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
# Docker CLI on Windows requires native bind paths when Git Bash conversion is disabled.
case "$(uname -s)" in MINGW*|MSYS*) tool_dir=$(cygpath -m "$tool_dir"); reports_dir=$(cygpath -m "$reports_dir"); export MSYS_NO_PATHCONV=1 ;; esac
docker run --rm --network none --mount "type=bind,source=$tool_dir,target=/tools,readonly" --mount "type=bind,source=$reports_dir,target=/reports" 'python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea' python /tools/demo_preflight.py "$demo" "$profile" "/reports/$report_id.json"
