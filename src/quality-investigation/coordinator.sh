#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
action=${1:-controlled}
export QUALITY_REPORT_DIR=${QUALITY_REPORT_DIR:?Set a fresh report directory}
mkdir -p "$QUALITY_REPORT_DIR"
gradle=/opt/munarium/clients/java/gradlew
app=/app/build/install/quality-investigation/bin/quality-investigation
if [ "$action" = qualify ]; then
  status=0
  "$gradle" -p /opt/munarium/clients/java --offline --no-daemon --max-workers=2 build conformanceTest > "$QUALITY_REPORT_DIR/gradle.log" 2>&1 || status=$?
  cp -R /opt/munarium/clients/java/build/test-results "$QUALITY_REPORT_DIR/sdk"
  cat "$QUALITY_REPORT_DIR/gradle.log"
  "$app" reports "$QUALITY_REPORT_DIR/sdk" 1
  exit "$status"
fi
case "$action" in unit|controlled|cloud-openai|cloud-anthropic|cloud-openrouter|restarted) ;; *) echo 'Unknown test action' >&2; exit 2 ;; esac
export QUALITY_TEST_KIND=$action
case "$action" in cloud-*) "$app" usage "$QUALITY_REPORT_DIR/before" ;; esac
status=0
"$gradle" --offline --no-daemon --max-workers=2 test > "$QUALITY_REPORT_DIR/gradle.log" 2>&1 || status=$?
cat "$QUALITY_REPORT_DIR/gradle.log"
case "$action" in cloud-*) "$app" usage "$QUALITY_REPORT_DIR/after" ;; esac
"$app" reports "$QUALITY_REPORT_DIR/junit" 0
exit "$status"
