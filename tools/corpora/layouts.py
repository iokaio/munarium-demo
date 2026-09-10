# SPDX-License-Identifier: Apache-2.0
# Modified 2026-09-09 for the standalone demo corpus import.
"""Per-corpus upload layouts for the demo bulk load.

A layout maps local corpus files to the logical upload paths the committed
runbooks bind (`server/runbooks/experiments/*.yaml`). Document BYTES are never
touched — sharding (hash buckets, year prefixes) is purely a blob-path
concern, so the lab suites' byte-anchored metadata regexes stay valid.

Each layout yields (logical_path, local_path, media_type) and declares
COLLECTION_PREFIXES — the exact `filenamePrefix` strings of its runbook's
collections — so the loader can prove, before a single byte moves, that every
file lands under exactly one declared collection prefix. Files that match no
prefix are a layout bug and fail the preflight.

Answer keys, manifests, index DBs, generators, and downloaders are never
enumerated: layouts walk only the source trees listed here. That rule does
real work for the three 2026-08-29 corpora: financial_advisory's
`sources/00_corpus_admin/manifest.json` is a 568 KB inventory of every doc
id, type and as-of date sitting INSIDE the source tree, patent_analysis's
`sources/manifest.json` names which target carries which planted defect,
and `sources/cache/` is 9 GB of USPTO zips. None of them is a corpus
document; each layout enumerates its subtrees explicitly rather than
walking a parent.
"""

from __future__ import annotations

import hashlib
from pathlib import Path

MEDIA_TYPES = {
    ".md": "text/markdown",
    ".txt": "text/plain",
    ".pdf": "application/pdf",
    ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ".json": "application/json",
    ".yaml": "text/yaml",
    ".yml": "text/yaml",
    ".csv": "text/csv",
    ".html": "text/html",
}


def media_type(path: Path) -> str:
    return MEDIA_TYPES.get(path.suffix.lower(), "application/octet-stream")


def _bucket(name: str, n: int) -> int:
    """Deterministic shard bucket: sha256(basename) % n.

    Must stay stable forever — the history-revolution v2 runbook's gw/
    jefferson collection prefixes are defined over exactly this function.
    """
    return int(hashlib.sha256(name.encode("utf-8")).hexdigest(), 16) % n


def _walk(root: Path):
    for p in sorted(root.rglob("*")):
        if p.is_file():
            yield p


def _walk_md(root: Path):
    """Markdown only. The three 2026-08-29 corpora are pure .md trees that
    keep non-document siblings (FTS index .db files, a JSON manifest) in the
    same directories, and those must never be uploaded."""
    for p in _walk(root):
        if p.suffix.lower() == ".md":
            yield p


# ---------------------------------------------------------------------------
# history_revolution — LOC American Revolution (66,739 docs / 527 MB)
# ---------------------------------------------------------------------------
#
# Shard counts are balanced by BYTES, not document count, because index-build
# cost tracks chunks. The two are wildly uncorrelated in this corpus: amnotes
# is 150 MB across 3,925 documents (~39 KB each) while gw is 39 MB across
# 30,170 (~1.4 KB each). Measured 2026-08-20, target ~10 MB per collection,
# which built in 11-23 s locally and leaves margin under the Azure Container
# Apps request window.
#
# This ONE table is the source of truth for both the upload paths and the
# runbook's collection list — regenerate the YAML with emit_history_yaml.py
# so the two can never drift.

HISTORY_SOURCES = "data/corpora/history"

HISTORY_TARGET_MB = 10

# (group path, lab source dirs, shard count) — group path becomes
# rev/<group>/[b<k>/]<labdir>/<file>. The demo derives its UI badge from the
# SECOND path segment, so founders/narrative/printed/maps/newspapers stay
# visible regardless of sharding.
HISTORY_GROUPS: list[tuple[str, list[str], int]] = [
    ("founders/gw", ["gw"], 4),  # 39.4 MB
    ("founders/jefferson", ["jefferson"], 1),  # 3.5 MB
    ("founders/misc", ["hamilton", "franklin", "madison"], 1),  # 2.5 MB
    ("narrative/amnotes", ["amnotes"], 15),  # 150.0 MB — the byte-heavy one
    ("narrative/capbay", ["capbay"], 5),  # 44.1 MB
    ("narrative/other", ["rarebook", "adams-trial"], 1),  # 3.0 MB
    ("printed/ephemera", ["ephemera"], 2),  # 14.8 MB
    ("printed/lawmaking", ["lawmaking"], 2),  # 15.7 MB
    ("printed/contcong", ["contcong"], 1),  # 3.3 MB
    ("maps", ["revmaps", "battlemaps", "rochambeau"], 1),  # 2.6 MB
]

# Newspapers are uploaded as their own pass (they are half the bytes), but
# they are collections of the same runbook.
NEWSPAPER_GROUP: tuple[str, list[str], int] = ("newspapers", ["newspapers"], 25)  # 247.7 MB


def _group_prefixes(group: str, shards: int) -> list[str]:
    """Collection filenamePrefixes for one group. The trailing slash is load-
    bearing: it keeps `b1/` from prefix-matching `b10/`."""
    if shards == 1:
        return [f"rev/{group}/"]
    return [f"rev/{group}/b{i}/" for i in range(shards)]


def _group_files(repo_root: Path, group: str, dirs: list[str], shards: int):
    root = repo_root / HISTORY_SOURCES
    for d in dirs:
        cdir = root / d
        if not cdir.is_dir():
            raise SystemExit(f"missing corpus dir: {cdir} (fetch the corpus first)")
        for p in _walk(cdir):
            # The lab dir stays in the path so basenames cannot collide when
            # several dirs merge into one group.
            tail = f"{d}/{p.name}"
            if shards == 1:
                logical = f"rev/{group}/{tail}"
            else:
                logical = f"rev/{group}/b{_bucket(p.name, shards)}/{tail}"
            yield logical, p, media_type(p)


HISTORY_PREFIXES = [
    pfx for group, _, shards in HISTORY_GROUPS for pfx in _group_prefixes(group, shards)
]
NEWSPAPER_PREFIXES = _group_prefixes(NEWSPAPER_GROUP[0], NEWSPAPER_GROUP[2])


def layout_history(repo_root: Path):
    """The core collections (everything except newspapers)."""
    for group, dirs, shards in HISTORY_GROUPS:
        yield from _group_files(repo_root, group, dirs, shards)


def layout_history_newspapers(repo_root: Path):
    """Chronicling America OCR pages, hash-sharded by bytes."""
    yield from _group_files(repo_root, *NEWSPAPER_GROUP)


# ---------------------------------------------------------------------------
# support_knowledge — the ten Nimbara source systems (1,500 docs)
# ---------------------------------------------------------------------------

SUPPORT_SOURCES = "data/corpora/support"
SUPPORT_SYSTEMS = [
    "bugtrail",
    "buildforge",
    "chatops",
    "community",
    "contractstore",
    "deskway",
    "docvault",
    "helphub",
    "mailarchive",
    "statusdesk",
]
SUPPORT_PREFIXES = [f"support/{s}/" for s in SUPPORT_SYSTEMS]


def layout_support(repo_root: Path):
    root = repo_root / SUPPORT_SOURCES
    for system in SUPPORT_SYSTEMS:
        sdir = root / system
        if not sdir.is_dir():
            raise SystemExit(
                f"missing corpus dir: {sdir} (regenerate: py generator/gen_corpus.py from the repo root)"
            )
        for p in _walk(sdir):
            rel = p.relative_to(sdir).as_posix()
            yield f"support/{system}/{rel}", p, media_type(p)


# ---------------------------------------------------------------------------
# due_diligence — the committed Northgate data room (612 docs + index doc)
# ---------------------------------------------------------------------------

DD_SOURCES = "data/corpora/dd"
DD_FOLDERS = [
    "01_corporate",
    "02_equity",
    "03_finance",
    "04_tax",
    "05_commercial",
    "06_employment",
    "07_intellectual_property",
    "08_legal_compliance",
    "09_privacy_security",
    "10_real_estate_environment",
    "11_insurance",
    "12_operations_regulatory",
]
DD_PREFIXES = [f"northgate/{f}/" for f in DD_FOLDERS] + ["northgate/000_"]


def layout_dd(repo_root: Path):
    root = repo_root / DD_SOURCES
    index_doc = root / "000_data_room_index.md"
    if not index_doc.is_file():
        raise SystemExit(f"missing {index_doc}")
    yield "northgate/000_data_room_index.md", index_doc, "text/markdown"
    for folder in DD_FOLDERS:
        fdir = root / folder
        if not fdir.is_dir():
            raise SystemExit(f"missing corpus dir: {fdir}")
        for p in _walk(fdir):
            rel = p.relative_to(fdir).as_posix()
            yield f"northgate/{folder}/{rel}", p, media_type(p)


# ---------------------------------------------------------------------------
# financial_advisory — the committed Vale ten-year record (1,232 docs + index)
# ---------------------------------------------------------------------------
#
# The lab folder names ARE the runbook's collection prefixes, so this is the
# plain mirror. Two files in the tree are deliberately not documents:
# 00_corpus_admin/manifest.json (the machine inventory) and the gitignored
# finance_index.db / finance_smoke_index.db FTS indexes — _walk_md drops the
# .db files and the index doc is yielded by name.

FIN_SOURCES = "data/corpora/fin"
FIN_FOLDERS = [
    "01_client_profile",
    "02_advisory_reviews",
    "03_constraints",
    "04_investment_policy",
    "05_portfolios",
    "06_businesses",
    "07_venture_capital",
    "08_tax_insurance",
    "09_real_estate_ventures",
    "10_hospitality_ventures",
    "11_transactions_and_financing",
    "12_historical_household",
    "13_consolidated_business_interests",
]
FIN_PREFIXES = ["vale/00_corpus_admin/"] + [f"vale/{f}/" for f in FIN_FOLDERS]


def layout_fin(repo_root: Path):
    root = repo_root / FIN_SOURCES
    # CORPUS_INDEX.md is a navigation document, the same role as the Northgate
    # data room's 000_ index, and it IS uploaded. manifest.json beside it is
    # not: it lists every doc id, type and as-of date in one file, which would
    # hand a model the corpus map instead of making it retrieve.
    index_doc = root / "00_corpus_admin" / "CORPUS_INDEX.md"
    if not index_doc.is_file():
        raise SystemExit(f"missing {index_doc}")
    yield "vale/00_corpus_admin/CORPUS_INDEX.md", index_doc, "text/markdown"
    for folder in FIN_FOLDERS:
        fdir = root / folder
        if not fdir.is_dir():
            raise SystemExit(f"missing corpus dir: {fdir}")
        for p in _walk_md(fdir):
            rel = p.relative_to(fdir).as_posix()
            yield f"vale/{folder}/{rel}", p, media_type(p)


# ---------------------------------------------------------------------------
# patent_analysis — real USPTO prosecution records (266 docs)
# ---------------------------------------------------------------------------
#
# Loads from sources-epoch2/, which is COMMITTED, rather than corpus_text/,
# which is gitignored and needs a USPTO ODP key plus a 9 GB zip cache to
# rebuild. The two trees were compared file-by-file: every one of
# corpus_text/'s 252 documents is present in sources-epoch2/ byte-identical,
# plus 14 real second office actions (notices/f2_*.md). So this is the same
# corpus the ask arm measured, with the epoch-2 chronology story included.
# epoch2.json is metadata about the slice, not a document.

PAT_SOURCES = "data/corpora/patents"
PAT_DIRS = ["prior_art", "decoys", "targets", "notices", "assessments"]
PAT_PREFIXES = [f"patents/{d}/" for d in PAT_DIRS]


def layout_patents(repo_root: Path):
    root = repo_root / PAT_SOURCES
    for d in PAT_DIRS:
        ddir = root / d
        if not ddir.is_dir():
            raise SystemExit(
                f"missing corpus dir: {ddir} "
                "(sources-epoch2 is committed; check the checkout, do not re-fetch)"
            )
        for p in _walk_md(ddir):
            rel = p.relative_to(ddir).as_posix()
            yield f"patents/{d}/{rel}", p, media_type(p)


# ---------------------------------------------------------------------------
# threat_intelligence — 64 multi-vendor reports, vendor read off the filename
# ---------------------------------------------------------------------------
#
# The only one of the three that needs mapping logic: the lab corpus is FLAT
# (rNN_<vendor>_<doctype>__<actor>__<seq>.md) while the runbook splits by
# vendor, because the vendor feed is the compartment boundary — three
# commercial feeds at level 1 under `intel`, public CERT advisories at 0.
# The per-vendor counts are asserted, so a renamed or added report fails the
# preflight instead of landing in the wrong compartment.

INTEL_SOURCES = "data/corpora/intel"
INTEL_VENDOR_COUNTS = {"helioscope": 23, "trellium": 18, "northwind": 18, "cert": 5}
INTEL_PREFIXES = [f"intel/{v}/" for v in INTEL_VENDOR_COUNTS]


def layout_intel(repo_root: Path):
    root = repo_root / INTEL_SOURCES
    if not root.is_dir():
        raise SystemExit(f"missing corpus dir: {root}")
    seen = {v: 0 for v in INTEL_VENDOR_COUNTS}
    for p in _walk_md(root):
        parts = p.name.split("_")
        vendor = parts[1] if len(parts) > 1 else ""
        if vendor not in INTEL_VENDOR_COUNTS:
            raise SystemExit(
                f"cannot read a vendor from {p.name}: expected "
                f"r<NN>_<{'|'.join(INTEL_VENDOR_COUNTS)}>_..."
            )
        seen[vendor] += 1
        yield f"intel/{vendor}/{p.name}", p, media_type(p)
    if seen != INTEL_VENDOR_COUNTS:
        raise SystemExit(
            f"vendor counts changed: expected {INTEL_VENDOR_COUNTS}, walked {seen} "
            "— update INTEL_VENDOR_COUNTS deliberately, and check the runbook's "
            "compartments still describe the feeds"
        )


LAYOUTS = {
    "history": (layout_history, HISTORY_PREFIXES, "history-revolution core (no newspapers)"),
    "history-newspapers": (
        layout_history_newspapers,
        NEWSPAPER_PREFIXES,
        "history-revolution Chronicling America pages",
    ),
    "support": (layout_support, SUPPORT_PREFIXES, "support_knowledge (Nimbara)"),
    "dd": (layout_dd, DD_PREFIXES, "due_diligence Northgate data room"),
    "fin": (layout_fin, FIN_PREFIXES, "financial_advisory Vale ten-year record"),
    "patents": (layout_patents, PAT_PREFIXES, "patent_analysis USPTO records (epoch-2 tree)"),
    "intel": (layout_intel, INTEL_PREFIXES, "threat_intelligence multi-vendor reports"),
}
