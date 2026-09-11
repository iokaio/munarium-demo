#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$(dirname "$0")"
action=${1:-test}; project=${2:-inventory-wave3}
case "$project" in inventory-?*) ;; *) exit 2 ;; esac
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
report="/work/$action/$run"; local_report="../../artifacts/inventory-replenishment/$action/$run"
mkdir -p "$local_report"
docker ps --format '{{.Names}} {{.Image}} {{.Status}}' > "$local_report/containers-before.txt"
git rev-parse HEAD > "$local_report/demo-revision.txt"
docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' > "$local_report/host.txt"
echo "POSIX local.sh $action $project; $(uname -s -m)" > "$local_report/command.txt"
docker build -t munarium-inventory-matrix:local 'https://github.com/iokaio/munarium.git#bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3:matrix'
docker image inspect munarium-inventory-matrix:local --format '{{.Id}} {{.Size}}' > "$local_report/matrix-image.txt"
compose build tests
docker image inspect munarium-inventory-runner:local --format '{{.Id}} {{.Size}}' > "$local_report/runner.txt"
compose run --rm --no-deps unit unit --work "$report/unit"
compose up -d server matrix provider-fixture sdk-fixture faults
compose run --rm --no-deps generator
compose run --rm --no-deps seed
if [ "$action" = cloud ]; then
  failed=0
  for provider in openai anthropic openrouter; do
    if compose run --rm --no-deps bootstrap bootstrap --provider "$provider" --work "$report/$provider/bootstrap"; then
      compose run --rm --no-deps tests usage --work "$report/$provider/usage-before" || failed=1
      compose run --rm --no-deps tests cloud --work "$report/$provider" || failed=1
      compose run --rm --no-deps tests usage --work "$report/$provider/usage-after" || failed=1
    else failed=1; fi
  done
  echo "Reports: artifacts/inventory-replenishment/cloud/$run"
  exit "$failed"
fi
compose run --rm --no-deps bootstrap bootstrap --work "$report/bootstrap"
compose run --rm --no-deps tests controlled --work "$report/controlled"
compose stop inventory-db
trap 'compose start inventory-db' EXIT
compose run --rm --no-deps tests sourceoutage --work "$report/sourceoutage"
compose start inventory-db
trap - EXIT
compose restart server
compose run --rm --no-deps server-ready
compose restart matrix
compose run --rm --no-deps tests restarted --work "$report/restarted"
compose run --rm --no-deps tests sdk --work "$report/sdk"
compose run --rm --no-deps tests matrixsdk --work "$report/matrixsdk"
compose run --rm --no-deps --entrypoint /usr/bin/python3 app /app/support.py render "$report/controlled/case-001/inventory.md" "$report/application.png"
echo "Reports: artifacts/inventory-replenishment/test/$run"
