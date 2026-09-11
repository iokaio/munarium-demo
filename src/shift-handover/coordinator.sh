#!/bin/sh
# SPDX-License-Identifier: Apache-2.0
set -eu
action=${1:-controlled}
report=${SHIFT_REPORT_DIR:?Set a fresh report directory}
mkdir -p "$report"
app=/opt/target/debug/shift-handover
case "$action" in
  unit)
    cargo fmt -p shift-handover -- --check > "$report/format.log" 2>&1
    cargo clippy --locked --offline --all-targets -- -D warnings > "$report/clippy.log" 2>&1
    status=0
    cargo test --locked --offline > "$report/unit.log" 2>&1 || status=$?
    cat "$report/unit.log"
    python3 /app/support.py report "$report/unit.log" "$report/tests.xml" unit
    # Two separate generator processes, each without network; compare all bytes.
    for profile in default heldout stress; do
      "$app" generate "/tmp/$profile-a" "/tmp/$profile-oracle-a" "$profile"
      "$app" generate "/tmp/$profile-b" "/tmp/$profile-oracle-b" "$profile"
      diff -r "/tmp/$profile-a" "/tmp/$profile-b"
      diff -r "/tmp/$profile-oracle-a" "/tmp/$profile-oracle-b"
      cp "/tmp/$profile-a/manifest.json" "$report/reproducible-$profile-manifest.json"
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
  controlled|restarted)
    exec "$app" qualify "$action" "$report" ;;
  *) echo 'Unknown test action' >&2; exit 2 ;;
esac
