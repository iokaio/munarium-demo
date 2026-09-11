#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
action=${1:-test}
report_dir=${POLICY_REPORT_DIR:-/work/controlled}
mkdir -p "$report_dir"
case "$action" in
  unit) filter='Kind=unit'; report=unit ;;
  test) filter='Kind=integration|Kind=model|Kind=headless'; report=application ;;
  cloud)
    case "$2" in
      openai) filter='FullyQualifiedName~CloudTests.Openai' ;;
      anthropic) filter='FullyQualifiedName~CloudTests.Anthropic' ;;
      openrouter) filter='FullyQualifiedName~CloudTests.Openrouter' ;;
      *) echo 'Unknown cloud provider' >&2; exit 2 ;;
    esac
    report=cloud ;;
  preview) filter='Kind=preview'; report=preview ;;
  qualify)
    dotnet test /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=sdk-unit.trx' --results-directory "$report_dir"
    dotnet test /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Conformance -c Release --no-build --no-restore --logger 'trx;LogFileName=sdk-conformance.trx' --results-directory "$report_dir"
    dotnet Policy.Harness/bin/Release/net10.0/Policy.Harness.dll reports "$report_dir/sdk-unit.trx" 0
    dotnet Policy.Harness/bin/Release/net10.0/Policy.Harness.dll reports "$report_dir/sdk-conformance.trx" 2
    exit ;;
  *) echo 'Unknown test action' >&2; exit 2 ;;
esac
test_exit=0
dotnet test tests/Policy.Tests.csproj -c Release --no-build --no-restore --filter "$filter" --logger "trx;LogFileName=$report.trx" --results-directory "$report_dir" || test_exit=$?
dotnet Policy.Harness/bin/Release/net10.0/Policy.Harness.dll reports "$report_dir/$report.trx" 0
exit "$test_exit"
