#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$(dirname "$0")"
action=${1:-test}
project=${2:-orders-wave1}
case "$action" in test|cloud|stop) ;; *) echo 'Use test, cloud, or stop' >&2; exit 2 ;; esac
case "$project" in orders-?*) ;; *) echo 'Use an isolated orders- project name' >&2; exit 2 ;; esac
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
local_report="../../artifacts/order-exception-triage/$action/$run"
mkdir -p "$local_report"
docker ps --format '{{.Names}} {{.Image}} {{.Status}}' > "$local_report/containers-before.txt"
docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' > "$local_report/docker-host.txt"
git rev-parse HEAD > "$local_report/demo-revision.txt"
echo "POSIX local.sh $action $project; host=$(uname -s -m)" > "$local_report/command.txt"
compose build tests
docker image inspect munarium-orders-runner:local --format '{{.Id}} {{.Size}}' > "$local_report/runner-image.txt"
compose run --rm --no-deps -e "ORDER_REPORT_DIR=$report/unit" tests unit
compose up -d server provider-fixture
compose run --rm --no-deps generator
if [ "$action" = cloud ]; then
  failed=0
  for provider in openai anthropic openrouter; do
    if compose run --rm --no-deps bootstrap bootstrap "$provider" --approve; then
      compose run --rm --no-deps -e "ORDER_REPORT_DIR=$report/$provider" tests "cloud-$provider" || failed=1
    else failed=1; echo "$provider bootstrap failed; continuing" >&2; fi
  done
  echo "Reports: artifacts/order-exception-triage/cloud/$run"
  exit "$failed"
fi
compose run --rm --no-deps bootstrap
compose run --rm --no-deps -e "ORDER_REPORT_DIR=$report/controlled" tests controlled
compose restart server
compose run --rm --no-deps -e "ORDER_REPORT_DIR=$report/restarted" tests restarted
compose run --rm --no-deps -e "ORDER_REPORT_DIR=$report/sdk" tests qualify
compose run --rm --no-deps app render "$report/controlled/event-001/packets" "$report/application.png"
echo "Reports: artifacts/order-exception-triage/test/$run"
