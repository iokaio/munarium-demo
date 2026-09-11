#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
action=${1:-controlled}
export RECONCILE_REPORT_DIR=${RECONCILE_REPORT_DIR:?Set a fresh report directory}
mkdir -p "$RECONCILE_REPORT_DIR"
gradle=/opt/munarium/clients/java/gradlew
app=/app/build/install/master-data-reconciliation/bin/master-data-reconciliation
if [ "$action" = qualify ]; then
  status=0
  "$gradle" -p /opt/munarium/clients/java --offline --no-daemon --max-workers=2 build conformanceTest > "$RECONCILE_REPORT_DIR/gradle.log" 2>&1 || status=$?
  cp -R /opt/munarium/clients/java/build/test-results "$RECONCILE_REPORT_DIR/sdk"
  cat "$RECONCILE_REPORT_DIR/gradle.log"
  "$app" reports "$RECONCILE_REPORT_DIR/sdk" 1
  exit "$status"
fi
case "$action" in unit|controlled|cloud-openai|cloud-anthropic|cloud-openrouter|restarted) ;; *) echo 'Unknown test action' >&2; exit 2 ;; esac
export RECONCILE_TEST_KIND=$action
status=0
"$gradle" --offline --no-daemon --max-workers=2 test > "$RECONCILE_REPORT_DIR/gradle.log" 2>&1 || status=$?
cat "$RECONCILE_REPORT_DIR/gradle.log"
"$app" reports "$RECONCILE_REPORT_DIR/junit" 0
exit "$status"
