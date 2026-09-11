#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
action=${1:-controlled}
report=${ENGINEERING_REPORT_DIR:?Set a fresh report directory}
mkdir -p "$report"
app=/opt/target/debug/engineering-change-review
case "$action" in
  unit)
    cargo fmt -p engineering-change-review -- --check > "$report/format.log" 2>&1
    cargo clippy --locked --offline --all-targets -- -D warnings > "$report/clippy.log" 2>&1
    status=0
    cargo test --locked --offline > "$report/unit.log" 2>&1 || status=$?
    cat "$report/unit.log"
    python3 /app/support.py report "$report/unit.log" "$report/tests.xml" unit
    # Two separate generator processes, each without network; compare all bytes.
    for profile in default heldout stress; do
      "$app" generate "/tmp/fixtures-a-$profile" "/tmp/oracle-a-$profile" "$profile"
      "$app" generate "/tmp/fixtures-b-$profile" "/tmp/oracle-b-$profile" "$profile"
      diff -r "/tmp/fixtures-a-$profile" "/tmp/fixtures-b-$profile"
      diff -r "/tmp/oracle-a-$profile" "/tmp/oracle-b-$profile"
      cp "/tmp/fixtures-a-$profile/manifest.json" "$report/reproducible-$profile-manifest.json"
    done
    exit "$status" ;;
  qualify)
    cd /opt/munarium/clients/rust
    cargo fmt -p munarium-client -p munarium-client-conformance -- --check > "$report/format.log" 2>&1
    cargo clippy --workspace --all-targets --locked --offline -- -D warnings > "$report/clippy.log" 2>&1
    status=0
    cargo test --workspace --locked --offline > "$report/unit.log" 2>&1 || status=$?
    cat "$report/unit.log"
    python3 /app/support.py report "$report/unit.log" "$report/unit.xml" unit
    cargo run -p munarium-client-conformance --locked --offline -- --rest "$MUNARIUM_REST_URL" --grpc "$MUNARIUM_GRPC_URL" --mgmt-env --smoke > "$report/conformance.log" 2>&1 || status=$?
    cat "$report/conformance.log"
    python3 /app/support.py report "$report/conformance.log" "$report/conformance.xml" conformance
    printf '%s\n' 'Kernel chronology scenarios are not implemented in this Rust SDK suite; no skipped test is counted as a pass.' > "$report/coverage-gap.txt"
    exit "$status" ;;
  controlled|restarted|cloud-openai|cloud-anthropic|cloud-openrouter)
    exec "$app" qualify "$action" "$report" ;;
  *) echo 'Unknown test action' >&2; exit 2 ;;
esac
