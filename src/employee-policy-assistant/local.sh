#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
cd "$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
action=${1:-test}
project=${POLICY_PROJECT:-policy-wave1}
desktop_provider=fixture
if [ "$action" = desktop ]; then desktop_provider=${2:-fixture}; fi
case "$desktop_provider" in fixture|openai|anthropic|openrouter) ;; *) echo 'Unsupported provider' >&2; exit 2 ;; esac
case "$project" in policy-*) ;; *) echo 'Use a dedicated policy- project' >&2; exit 2 ;; esac
case "$project" in *[!a-z0-9-]*) echo 'Invalid project name' >&2; exit 2 ;; esac
case "$(docker context inspect --format '{{.Endpoints.docker.Host}}')" in unix://*|npipe://*) ;; *) echo 'Select a local Docker context' >&2; exit 2 ;; esac
test "$(docker info --format '{{.OSType}}')" = linux
compose() {
  case "$action" in
    cloud) docker compose --env-file ../../.env.local -p "$project" -f compose.yaml -f compose.cloud.yaml "$@" ;;
    desktop)
      case "$desktop_provider" in
        openai|anthropic|openrouter) docker compose --env-file ../../.env.local -p "$project" -f compose.yaml -f compose.cloud.yaml -f compose.desktop.yaml "$@" ;;
        *) docker compose --env-file ../../.env.local.sample -p "$project" -f compose.yaml -f compose.desktop.yaml "$@" ;;
      esac ;;
    *) docker compose --env-file ../../.env.local.sample -p "$project" -f compose.yaml "$@" ;;
  esac
}
case "$action" in test|cloud|desktop|publish|stop) ;; *) echo 'Unknown action' >&2; exit 2 ;; esac
compose config --quiet
if [ "$action" = stop ]; then compose stop; exit; fi
compose build tests
if [ "$action" = publish ]; then
  runtime=${2:-linux-x64}
  case "$runtime" in win-x64|linux-x64|linux-arm64|osx-x64|osx-arm64) ;; *) echo 'Unsupported runtime' >&2; exit 2 ;; esac
  compose run --rm --no-deps --entrypoint dotnet tests publish Policy.Desktop/Policy.Desktop.csproj -c Release -r "$runtime" --self-contained true -o "/work/desktop/$runtime"
  exit
fi
run=$(compose run --rm --no-deps --entrypoint sh tests -c 'cat /proc/sys/kernel/random/uuid')
compose run --rm --no-deps -e "POLICY_REPORT_DIR=/work/controlled/$run" tests unit
compose up -d server provider-fixture
compose run --rm --no-deps generator
if [ "$action" = cloud ]; then
  failed=0
  for provider in openai anthropic openrouter; do
    if compose run --rm --no-deps bootstrap bootstrap "$provider" --approve; then
      compose run --rm --no-deps -e "POLICY_REPORT_DIR=/work/cloud/$run/$provider" tests cloud "$provider" || failed=1
    else failed=1; fi
  done
  echo "Reports: artifacts/employee-policy-assistant/cloud/$run"
  exit "$failed"
fi
provider=fixture
if [ "$action" = desktop ]; then provider=$desktop_provider; fi
compose run --rm --no-deps bootstrap bootstrap "$provider" --approve
if [ "$action" = desktop ]; then
  compose run --rm --no-deps --entrypoint sh bootstrap -c 'mkdir -p /work/desktop/credentials && cp /credentials/*.json /work/desktop/credentials/'
  echo 'Desktop Server: http://127.0.0.1:18082; tutorial grants: artifacts/employee-policy-assistant/desktop/credentials'
  exit
fi
compose run --rm --no-deps -e "POLICY_REPORT_DIR=/work/$action/$run" tests test
if [ "$action" = test ]; then compose run --rm --no-deps -e "POLICY_REPORT_DIR=/work/test/$run/sdk" tests qualify; fi
echo "Reports: artifacts/employee-policy-assistant/$action/$run"
