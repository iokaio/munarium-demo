#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
demo=${1:?Demo required}; project=${2:?Fresh project required}; profile=${3:-default}
case "$demo:$project" in *[!a-z0-9:-]*) exit 2 ;; esac
case "$profile" in default|heldout|stress) ;; *) exit 2 ;; esac
tool_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(dirname "$tool_dir")
case "$(uname -s)" in MINGW*|MSYS*) export MSYS_NO_PATHCONV=1 ;; esac
test -f "$repo_dir/src/$demo/local.sh"
test -z "$(docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.ID}}')" || { echo 'Use a fresh project' >&2; exit 2; }
run="$(date -u +%Y%m%dT%H%M%SZ)-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
report="$repo_dir/artifacts/$demo/measurements/$run"
mkdir -p "$report"
started=$(date +%s)
monitor() {
    while :; do
        ids=$(docker ps --filter "label=com.docker.compose.project=$project" --format '{{.ID}}')
        if [ -n "$ids" ]; then
            # IDs originate from Docker, never from file content or a model.
            stamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)
            docker stats --no-stream --format '{{json .}}' $ids 2>/dev/null | while IFS= read -r row; do
                printf '{"utc":"%s","stats":%s}\n' "$stamp" "$row"
            done >> "$report/samples.jsonl" || true
        fi
        sleep 2
    done
}
monitor &
monitor_pid=$!
trap 'kill "$monitor_pid" 2>/dev/null || true' EXIT HUP INT TERM
status=0
DEMO_PROFILE="$profile" sh "$repo_dir/src/$demo/local.sh" test "$project" > "$report/workflow.log" 2>&1 || status=$?
kill "$monitor_pid" 2>/dev/null || true
wait "$monitor_pid" 2>/dev/null || true
trap - EXIT HUP INT TERM
elapsed=$(( $(date +%s) - started ))
docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.Image}}' | sort -u | while IFS= read -r name; do
    docker image inspect "$name" --format '{"id":{{json .Id}},"size":{{.Size}},"architecture":{{json .Architecture}},"os":{{json .Os}}}'
done > "$report/images.jsonl"
docker volume ls --filter "label=com.docker.compose.project=$project" --format '{{.Name}}' | while IFS= read -r volume; do
    size=$(docker run --rm --network none --mount "type=volume,source=$volume,target=/data,readonly" python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea du -sb /data)
    printf '%s\t%s\n' "$volume" "${size%%[[:space:]]*}"
done > "$report/volumes.tsv"
printf '{"demo":"%s","project":"%s","profile":"%s","exit_code":%s,"elapsed_seconds":%s,"sampling":"approximately every 3 seconds; short-lived peaks may be missed","download_measurement":"Cold downloads not measured; image sizes are unpacked local bytes"}\n' "$demo" "$project" "$profile" "$status" "$elapsed" > "$report/run.json"
echo "Measurement report: artifacts/$demo/measurements/$run"
tail -n 15 "$report/workflow.log"
case "$(uname -s)" in MINGW*|MSYS*) tool_dir=$(cygpath -m "$tool_dir"); report=$(cygpath -m "$report") ;; esac
docker run --rm --network none --mount "type=bind,source=$tool_dir,target=/tools,readonly" --mount "type=bind,source=$report,target=/report" python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea python /tools/summarize_demo.py /report
exit "$status"
