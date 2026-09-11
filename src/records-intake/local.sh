#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$(dirname "$0")"
action=${1:-test}; project=${2:-records-wave2}
case "$project" in records-?*) ;; *) exit 2 ;; esac
case "$project" in *[!a-z0-9-]*) exit 2 ;; esac
case "$(docker context inspect --format '{{.Endpoints.docker.Host}}')" in unix://*|npipe://*) ;; *) echo 'Select a local Docker context'; exit 2 ;; esac
test "$(docker info --format '{{.OSType}}')" = linux
compose() { docker compose --env-file ../../.env.local.sample -p "$project" -f compose.yaml "$@"; }
compose config --quiet
case "$action" in stop) compose stop; exit ;; cloud) echo 'Not applicable: records intake has no model calls. Use test for the complete suite.'; exit ;; test) ;; *) exit 2 ;; esac
run="$(date -u +%Y%m%dT%H%M%SZ)-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
report="/work/test/$run"; local_report="../../artifacts/records-intake/test/$run"
mkdir -p "$local_report"
docker ps --format '{{.Names}} {{.Image}} {{.Status}}' > "$local_report/containers-before.txt"
git rev-parse HEAD > "$local_report/demo-revision.txt"
docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' > "$local_report/host.txt"
echo "POSIX local.sh $action $project; $(uname -s -m)" > "$local_report/command.txt"
compose build tests
docker image inspect munarium-records-runner:local --format '{{.Id}} {{.Size}}' > "$local_report/runner.txt"
compose run --rm --no-deps -e "RECORDS_REPORT_DIR=$report/unit" unit unit
compose up -d server faults
compose run --rm --no-deps generator
compose run --rm --no-deps -e "RECORDS_RUN_ID=$run" bootstrap
compose run --rm --no-deps -e "RECORDS_REPORT_DIR=$report/controlled" tests controlled
compose restart server
compose run --rm --no-deps -e "RECORDS_REPORT_DIR=$report/controlled" tests restarted
compose run --rm --no-deps -e "RECORDS_REPORT_DIR=$report/sdk" tests qualify
compose run --rm --no-deps --entrypoint python3 app /app/render.py "$report/controlled/case-001/status.txt" "$report/application.png"
echo "Reports: artifacts/records-intake/test/$run"
