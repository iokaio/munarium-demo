#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
action=${1:-controlled}
export ORDER_REPORT_DIR=${ORDER_REPORT_DIR:?Set a fresh report directory}
mkdir -p "$ORDER_REPORT_DIR"
gradle=/opt/munarium/clients/java/gradlew
app=/app/build/install/order-exception-triage/bin/order-exception-triage
if [ "$action" = qualify ]; then
  status=0
  "$gradle" -p /opt/munarium/clients/java --offline --no-daemon --max-workers=2 build conformanceTest > "$ORDER_REPORT_DIR/gradle.log" 2>&1 || status=$?
  cp -R /opt/munarium/clients/java/build/test-results "$ORDER_REPORT_DIR/sdk"
  cat "$ORDER_REPORT_DIR/gradle.log"
  "$app" reports "$ORDER_REPORT_DIR/sdk" 1
  exit "$status"
fi
case "$action" in unit|controlled|cloud-openai|cloud-anthropic|cloud-openrouter|restarted) ;; *) echo 'Unknown test action' >&2; exit 2 ;; esac
export ORDER_TEST_KIND=$action
status=0
"$gradle" --offline --no-daemon --max-workers=2 test > "$ORDER_REPORT_DIR/gradle.log" 2>&1 || status=$?
cat "$ORDER_REPORT_DIR/gradle.log"
"$app" reports "$ORDER_REPORT_DIR/junit" 0
exit "$status"
