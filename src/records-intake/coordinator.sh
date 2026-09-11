#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
kind=${1:-controlled}
report=${RECORDS_REPORT_DIR:?Set a fresh report directory}
mkdir -p "$report"
app=/app/Records/bin/Release/net10.0/Records.dll
run_tests() {
  project=$1
  filter=$2
  name=$3
  skips=$4
  status=0
  dotnet test "$project" -c Release --no-build --no-restore --filter "$filter" --logger "trx;LogFileName=$name.trx" --results-directory "$report" > "$report/$name.log" 2>&1 || status=$?
  cat "$report/$name.log"
  dotnet "$app" reports "$report/$name.trx" "$skips"
  return "$status"
}
case "$kind" in
 unit)
  dotnet format Records/Records.csproj --no-restore --verify-no-changes > "$report/app-format.log" 2>&1
  dotnet format tests/Records.Tests.csproj --no-restore --verify-no-changes > "$report/format.log" 2>&1
  run_tests tests/Records.Tests.csproj 'Kind=unit' unit 0 ;;
 controlled)
  run_tests tests/Records.Tests.csproj 'Kind=business' business 0
  run_tests tests/Records.Tests.csproj 'Kind=failure' failure 0
  run_tests tests/Records.Tests.csproj 'Kind=prepare-restart' prepare-restart 0 ;;
 restarted) run_tests tests/Records.Tests.csproj 'Kind=restarted' restarted 0 ;;
 qualify)
  run_tests /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Tests '' sdk-unit 0
  run_tests /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Conformance '' sdk-conformance 2 ;;
 *) echo 'Unknown test action' >&2; exit 2 ;;
esac
