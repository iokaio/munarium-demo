#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$(dirname "$0")"
action=${1:-test}
if [ "$action" = cloud ] && [ "${DEMO_PROFILE:-default}" != default ]; then echo 'Cloud qualification uses the default corpus' >&2; exit 2; fi
project=${2:-invoice-wave1}
case "$action" in test|cloud|stop) ;; *) echo 'Use test, cloud, or stop.' >&2; exit 2;; esac
case "$project" in invoice-*) ;; *) echo 'Use a project beginning invoice-.' >&2; exit 2;; esac
case "$project" in *[!a-z0-9-]*) echo 'Project must contain lowercase letters, digits, and hyphens.' >&2; exit 2;; esac
endpoint=$(docker context inspect --format '{{.Endpoints.docker.Host}}')
case "$endpoint" in unix://*|npipe://*) ;; *) echo 'Select a local Docker context.' >&2; exit 2;; esac
[ "$(docker info --format '{{.OSType}}')" = linux ] || { echo 'Start Docker with Linux containers.' >&2; exit 2; }
compose() {
    if [ "$action" = cloud ]; then
        docker compose --env-file ../../.env.local -p "$project" -f compose.yaml -f compose.cloud.yaml "$@"
    else
        docker compose --env-file ../../.env.local.sample -p "$project" -f compose.yaml "$@"
    fi
}
if [ "$action" = cloud ] && [ ! -f ../../.env.local ]; then
    echo 'Copy .env.local.sample to .env.local; supply all three provider keys and preferred models.' >&2
    exit 2
fi
compose config --quiet
if [ "$action" = stop ]; then compose stop; exit; fi
sh ../../tools/demo_preflight.sh invoice-exception "${DEMO_PROFILE:-default}"
test_run="$(date -u +%Y%m%dT%H%M%SZ)-$(od -An -N8 -tx1 /dev/urandom | tr -d ' \n')"
test_report="/work/test/$test_run"
compose build tests
compose run --rm --no-deps tests unit --work "$test_report"
compose up -d server provider-fixture sdk-fixture
compose run --rm --no-deps generator
if [ "$action" = cloud ]; then
    cloud_run="$(date -u +%Y%m%dT%H%M%SZ)-$$"
    cloud_failed=0
    for cloud_provider in openai anthropic openrouter; do
        if compose run --rm --no-deps bootstrap bootstrap --approve --provider "$cloud_provider" --preferred-model; then
            if ! compose run --rm --no-deps app process --cloud-run "$cloud_run"; then cloud_failed=1; fi
            if ! compose run --rm --no-deps tests cloud-test --cloud-run "$cloud_run"; then cloud_failed=1; fi
        else
            cloud_failed=1
            echo "$cloud_provider bootstrap failed; continuing with remaining providers." >&2
        fi
    done
    echo "Cloud reports: artifacts/invoice-exception/cloud/$cloud_run"
    exit "$cloud_failed"
fi
compose run --rm --no-deps bootstrap
if [ "$action" = test ]; then
    compose run --rm --no-deps tests test --work "$test_report"
    compose run --rm --no-deps tests qualify --work "$test_report"
fi
compose run --rm --no-deps app process --work "$test_report"
compose run --rm --no-deps tests quality --work "$test_report"
echo "Reports: artifacts/invoice-exception/test/$test_run"
