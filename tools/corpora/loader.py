# SPDX-License-Identifier: Apache-2.0
# Modified 2026-09-09 for the standalone demo corpus import.
"""loader.py — layout-aware bulk corpus loader for the munarium demo.

Front end over the server's bulk upload sessions (POST /v1/ingest/bulk et al,
2026-08-19): walks a lab corpus, applies the corpus's upload layout
(layouts.py — hash-bucketed shards, year prefixes, answer-key exclusion),
opens a session with the full manifest, streams chunks, and finalizes.
Resumability and per-document idempotency come from the SERVER (the session
diff and `needed` list); the local JSONL log is an audit record, not a ledger.

Stdlib only (urllib), matching the repo's dependency posture.

Usage (from anywhere; paths resolve from the repo root):
  py loader.py --corpus dd --dry-run --report-prefixes
  py loader.py --corpus support
  py loader.py --corpus history --only-prefix rev/printed/,rev/narrative/other/
  py loader.py --corpus history --resume blk-...
  py loader.py --corpus history-newspapers

Env:
  MUNARIUM_BASE_URL   (default http://localhost:8080)
  MUNARIUM_TOKEN      bearer token with ingest rights
  MUNARIUM_UID        X-Munarium-Uid (default bulk-loader)
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

from layouts import LAYOUTS

REPO_ROOT = Path(__file__).resolve().parents[2]
CHUNK_FILES_MAX = 500


def request(method: str, url: str, token: str, uid: str, body=None, timeout=590):
    data = None
    headers = {"Authorization": f"Bearer {token}", "X-Munarium-Uid": uid}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.status, json.loads(resp.read().decode("utf-8"))


def request_retry(method, url, token, uid, body=None, attempts=4):
    for attempt in range(1, attempts + 1):
        try:
            return request(method, url, token, uid, body)
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")
            if e.code >= 500 and attempt < attempts:
                print(f"  attempt {attempt}: HTTP {e.code}; retrying...", file=sys.stderr)
                time.sleep(5 * attempt)
                continue
            raise SystemExit(f"{method} {url} -> HTTP {e.code}: {detail}")
        except (urllib.error.URLError, TimeoutError) as e:
            if attempt < attempts:
                print(f"  attempt {attempt}: {e}; retrying...", file=sys.stderr)
                time.sleep(5 * attempt)
                continue
            raise SystemExit(f"{method} {url} -> {e}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--corpus", required=True, choices=sorted(LAYOUTS))
    ap.add_argument("--root", default=str(REPO_ROOT), help="repo root (default: derived)")
    ap.add_argument("--base-url", default=os.environ.get("MUNARIUM_BASE_URL", "http://localhost:8080"))
    ap.add_argument("--uid", default=os.environ.get("MUNARIUM_UID", "bulk-loader"))
    ap.add_argument("--label", default=None)
    ap.add_argument("--resume", default=None, metavar="BULK_ID")
    ap.add_argument("--only-prefix", default=None, help="comma-separated logical-path prefixes to include")
    ap.add_argument("--chunk-files", type=int, default=400)
    ap.add_argument("--chunk-bytes", type=int, default=140_000_000)
    ap.add_argument("--dry-run", action="store_true", help="no network calls")
    ap.add_argument("--report-prefixes", action="store_true", help="print per-collection-prefix counts")
    ap.add_argument("--log", default=None, help="JSONL audit log path")
    args = ap.parse_args()

    layout_fn, prefixes, describe = LAYOUTS[args.corpus]
    root = Path(args.root)
    base = args.base_url.rstrip("/")
    chunk_files = min(args.chunk_files, CHUNK_FILES_MAX)
    only = [p.strip() for p in args.only_prefix.split(",")] if args.only_prefix else None

    # ---- enumerate + preflight ------------------------------------------
    print(f"enumerating {describe} ...")
    files = []  # (logical, path, media)
    seen = set()
    for logical, path, media in layout_fn(root):
        if only and not any(logical.startswith(p) for p in only):
            continue
        if logical in seen:
            raise SystemExit(f"layout bug: duplicate logical path {logical}")
        seen.add(logical)
        files.append((logical, path, media))
    if not files:
        raise SystemExit("nothing to upload (check --only-prefix)")

    # Every file must land under exactly one declared collection prefix.
    counts: dict[str, int] = {p: 0 for p in prefixes}
    unmatched = []
    for logical, _, _ in files:
        hits = [p for p in prefixes if logical.startswith(p)]
        if len(hits) != 1:
            unmatched.append(logical)
        else:
            counts[hits[0]] += 1
    if unmatched:
        for u in unmatched[:10]:
            print(f"  NO COLLECTION PREFIX: {u}", file=sys.stderr)
        raise SystemExit(
            f"{len(unmatched)} file(s) match no (or multiple) declared collection prefixes — fix layouts.py first"
        )
    if args.report_prefixes or args.dry_run:
        print(f"{len(files)} files across {sum(1 for c in counts.values() if c)} collection prefixes:")
        for p in prefixes:
            if counts[p] or not only:
                print(f"  {counts[p]:>7}  {p}")
    if args.dry_run:
        print("dry run: no network calls made.")
        return 0

    token = os.environ.get("MUNARIUM_TOKEN")
    if not token:
        raise SystemExit("set MUNARIUM_TOKEN to a token with ingest rights")

    log_path = Path(args.log) if args.log else Path.cwd() / f"bulkload-{args.corpus}.jsonl"
    log = open(log_path, "a", encoding="utf-8")

    def audit(kind, **kw):
        log.write(json.dumps({"t": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "event": kind, **kw}) + "\n")
        log.flush()

    # ---- open / resume the session --------------------------------------
    by_name = {logical: (path, media) for logical, path, media in files}
    if args.resume:
        bulk_id = args.resume
        _, status = request_retry(
            "GET", f"{base}/v1/ingest/bulk/{bulk_id}?include_needed=true", token, args.uid
        )
        if status.get("status") != "open":
            raise SystemExit(f"session {bulk_id} is {status.get('status')}; cannot resume")
        needed = status.get("needed") or []
        print(f"resuming {bulk_id}: {len(needed)} of {status.get('total')} still needed")
    else:
        print(f"hashing {len(files)} files for the manifest ...")
        manifest = []
        for logical, path, media in files:
            data = path.read_bytes()
            manifest.append(
                {
                    "filename": logical,
                    "sha256": hashlib.sha256(data).hexdigest(),
                    "bytes_len": len(data),
                    "media_type": media,
                }
            )
        body = {"files": manifest}
        if args.label:
            body["label"] = args.label
        _, opened = request_retry("POST", f"{base}/v1/ingest/bulk", token, args.uid, body)
        bulk_id = opened["bulk_id"]
        needed = opened.get("needed") or []
        print(
            f"session {bulk_id}: {opened['total']} total, "
            f"{opened['already_present']} already present, {len(needed)} needed"
        )
        audit("open", bulk_id=bulk_id, total=opened["total"], needed=len(needed))

    # ---- stream chunks ---------------------------------------------------
    sent = 0
    chunk, chunk_size = [], 0

    def flush():
        nonlocal sent, chunk, chunk_size
        if not chunk:
            return
        n = len(chunk)
        _, resp = request_retry(
            "POST", f"{base}/v1/ingest/bulk/{bulk_id}/chunk", token, args.uid, {"files": chunk}
        )
        sent += n
        failed = [r for r in resp.get("results", []) if r.get("error")]
        print(
            f"  chunk ok ({n} files; {sent}/{len(needed)}): stored {resp['stored']} "
            f"skipped {resp['skipped_existing']} pending {resp['pending']} failed {resp['failed']}"
        )
        for r in failed[:5]:
            print(f"    FAILED {r['filename']}: {r['error']}", file=sys.stderr)
        audit("chunk", bulk_id=bulk_id, files=n, failed=len(failed))
        chunk, chunk_size = [], 0

    for logical in needed:
        entry = by_name.get(logical)
        if entry is None:
            print(f"  WARNING: server needs '{logical}' but the layout does not produce it", file=sys.stderr)
            continue
        path, media = entry
        data = path.read_bytes()
        if chunk and (len(chunk) >= chunk_files or chunk_size + len(data) > args.chunk_bytes):
            flush()
        chunk.append(
            {
                "filename": logical,
                "media_type": media,
                "content_base64": base64.b64encode(data).decode("ascii"),
            }
        )
        chunk_size += len(data)
    flush()

    # ---- finalize --------------------------------------------------------
    _, done = request_retry("POST", f"{base}/v1/ingest/bulk/{bulk_id}/complete", token, args.uid)
    print(json.dumps(done, indent=2))
    audit("complete", bulk_id=bulk_id, status=done.get("status"))
    if done.get("status") != "completed":
        print(
            f"INCOMPLETE — resume with: py loader.py --corpus {args.corpus} --resume {bulk_id}",
            file=sys.stderr,
        )
        return 1
    print(f"session {bulk_id} completed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
