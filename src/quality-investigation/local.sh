#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$(dirname "$0")"
action=${1:-test}
project=${2:-quality-wave2}
case "$action" in test|cloud|stop) ;; *) echo 'Use test, cloud, or stop' >&2; exit 2 ;; esac
case "$project" in quality-?*) ;; *) echo 'Use an isolated quality- project name' >&2; exit 2 ;; esac
case "$project" in *[!a-z0-9-]*) echo 'Invalid project name' >&2; exit 2 ;; esac
case "$(docker context inspect --format '{{.Endpoints.docker.Host}}')" in unix://*|npipe://*) ;; *) echo 'Select a local Docker context' >&2; exit 2 ;; esac
test "$(docker info --format '{{.OSType}}')" = linux
compose() {
  if [ "$action" = cloud ]; then docker compose --env-file ../../.env.local -p "$project" -f compose.yaml -f compose.cloud.yaml "$@"
  else docker compose --env-file ../../.env.local.sample -p "$project" -f compose.yaml "$@"; fi
}
compose config --quiet
if [ "$action" = stop ]; then compose stop; exit; fi
run="$(date -u +%Y%m%dT%H%M%SZ)-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
report="/work/$action/$run"
local_report="../../artifacts/quality-investigation/$action/$run"
mkdir -p "$local_report"
docker ps --format '{{.Names}} {{.Image}} {{.Status}}' > "$local_report/containers-before.txt"
docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' > "$local_report/docker-host.txt"
git rev-parse HEAD > "$local_report/demo-revision.txt"
echo "POSIX local.sh $action $project; host=$(uname -s -m)" > "$local_report/command.txt"
sh ../../tools/demo_preflight.sh quality-investigation "${DEMO_PROFILE:-default}"
compose build tests
docker image inspect munarium-quality-runner:local --format '{{.Id}} {{.Size}}' > "$local_report/runner-image.txt"
compose run --rm --no-deps -e "QUALITY_REPORT_DIR=$report/unit" unit unit
compose up -d server provider-fixture faults
compose run --rm --no-deps generator
if [ "$action" = cloud ]; then
  failed=0
  for provider in openai anthropic openrouter; do
    if compose run --rm --no-deps bootstrap bootstrap "$provider" --approve; then
      compose run --rm --no-deps -e "QUALITY_REPORT_DIR=$report/$provider" tests "cloud-$provider" || failed=1
    else failed=1; echo "$provider bootstrap failed; continuing" >&2; fi
  done
  echo "Reports: artifacts/quality-investigation/cloud/$run"
  exit "$failed"
fi
compose run --rm --no-deps bootstrap
compose run --rm --no-deps -e "QUALITY_REPORT_DIR=$report/controlled" tests controlled
compose restart server
compose run --rm --no-deps -e "QUALITY_REPORT_DIR=$report/restarted" tests restarted
compose run --rm --no-deps -e "QUALITY_REPORT_DIR=$report/sdk" tests qualify
compose run --rm --no-deps app render "$report/controlled/case-001/packet.md" "$report/application.png"
echo "Reports: artifacts/quality-investigation/test/$run"
