#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$(dirname "$0")"
action=${1:-test}; project=${2:-digest-wave2}
case "$project" in digest-?*) ;; *) exit 2 ;; esac
case "$project" in *[!a-z0-9-]*) exit 2 ;; esac
case "$(docker context inspect --format '{{.Endpoints.docker.Host}}')" in unix://*|npipe://*) ;; *) echo 'Select a local Docker context'; exit 2 ;; esac
test "$(docker info --format '{{.OSType}}')" = linux
case "$action" in test|stop) ;; cloud) test -f ../../.env.local ;; *) exit 2 ;; esac
compose() {
  if [ "$action" = cloud ]; then docker compose --env-file ../../.env.local -p "$project" -f compose.yaml -f compose.cloud.yaml "$@"
  else docker compose --env-file ../../.env.local.sample -p "$project" -f compose.yaml "$@"; fi
}
compose config --quiet
if [ "$action" = stop ]; then compose stop; exit; fi
run="$(date -u +%Y%m%dT%H%M%SZ)-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
report="/work/$action/$run"; local_report="../../artifacts/policy-change-digest/$action/$run"
mkdir -p "$local_report"
docker ps --format '{{.Names}} {{.Image}} {{.Status}}' > "$local_report/containers-before.txt"
git rev-parse HEAD > "$local_report/demo-revision.txt"
docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' > "$local_report/host.txt"
echo "POSIX local.sh $action $project; $(uname -s -m)" > "$local_report/command.txt"
sh ../../tools/demo_preflight.sh policy-change-digest "${DEMO_PROFILE:-default}"
compose build tests
docker image inspect munarium-digest-runner:local --format '{{.Id}} {{.Size}}' > "$local_report/runner.txt"
compose run --rm --no-deps unit unit --work "$report/unit"
compose up -d server provider-fixture sdk-fixture faults
compose run --rm --no-deps generator
if [ "$action" = cloud ]; then
  failed=0
  for provider in openai anthropic openrouter; do
    if compose run --rm --no-deps bootstrap bootstrap --provider "$provider" --work "$report/$provider/bootstrap"; then
      compose run --rm --no-deps tests usage --work "$report/$provider/usage-before" || failed=1
      compose run --rm --no-deps tests cloud --work "$report/$provider" || failed=1
      compose run --rm --no-deps tests usage --work "$report/$provider/usage-after" || failed=1
    else failed=1; fi
  done
  echo "Reports: artifacts/policy-change-digest/cloud/$run"
  exit "$failed"
fi
compose run --rm --no-deps bootstrap bootstrap --work "$report/bootstrap"
compose run --rm --no-deps tests controlled --work "$report/controlled"
compose restart server
compose run --rm --no-deps tests restarted --work "$report/restarted"
compose run --rm --no-deps tests sdk --work "$report/sdk"
compose run --rm --no-deps --entrypoint /usr/bin/python3 app /app/support.py render "$report/controlled/case-001/digest.md" "$report/application.png"
echo "Reports: artifacts/policy-change-digest/test/$run"
