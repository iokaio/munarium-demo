# SPDX-License-Identifier: Apache-2.0
"""Verify bundled corpus packs, optionally unpacking them for loader.py.

Python 3.11+, standard library only. No network or provider calls.
Existing files are accepted only when their hashes match the manifest.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import zipfile

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "data"


def validate_relative(relative: str) -> None:
    parts = PurePosixPath(relative)
    if not parts.parts or parts.is_absolute() or ".." in parts.parts or "\\" in relative or ":" in relative:
        raise ValueError(f"unsafe pack path: {relative!r}")


def safe_path(base: Path, relative: str) -> Path:
    validate_relative(relative)
    target = (base / relative).resolve()
    if not target.is_relative_to(base.resolve()) or target == base.resolve():
        raise ValueError(f"pack path escapes destination: {relative!r}")
    return target


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus", default="all", help="all, history, support, dd, fin, patents, intel")
    parser.add_argument("--verify-only", action="store_true", help="read and hash every member without extracting")
    args = parser.parse_args()
    manifest = json.loads((DATA / "manifest.json").read_text(encoding="utf-8"))
    corpora = [c for c in manifest["corpora"] if args.corpus in ("all", c["id"])]
    if not corpora:
        parser.error(f"unknown corpus: {args.corpus}")
    for corpus in corpora:
        count = total_bytes = 0
        seen: set[str] = set()
        destination = DATA / "corpora" / corpus["id"]
        for pack in corpus["packs"]:
            source = safe_path(DATA, pack["path"])
            with source.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            if source.stat().st_size != pack["bytes"] or digest != pack["sha256"]:
                raise ValueError(f"pack checksum mismatch: {pack['path']}")
            with zipfile.ZipFile(source) as archive:
                index = json.loads(archive.read("_manifest.json"))
                if index["corpus"] != corpus["id"] or len(index["documents"]) != pack["documents"]:
                    raise ValueError(f"pack inventory mismatch: {pack['path']}")
                names = archive.namelist()
                expected = {row["path"] for row in index["documents"]} | {"_manifest.json"}
                if len(names) != len(expected) or set(names) != expected:
                    raise ValueError(f"unexpected or duplicate archive members: {pack['path']}")
                for row in index["documents"]:
                    relative = row["path"]
                    validate_relative(relative)
                    if relative in seen:
                        raise ValueError(f"duplicate corpus path: {relative}")
                    seen.add(relative)
                    content = archive.read(relative)
                    if len(content) != row["bytes"] or hashlib.sha256(content).hexdigest() != row["sha256"]:
                        raise ValueError(f"document checksum mismatch: {corpus['id']}/{relative}")
                    if not args.verify_only:
                        target = safe_path(destination, relative)
                        target.parent.mkdir(parents=True, exist_ok=True)
                        if target.exists():
                            with target.open("rb") as stream:
                                existing = hashlib.file_digest(stream, "sha256").hexdigest()
                            if existing != row["sha256"]:
                                raise ValueError(f"refusing to overwrite changed document: {target}")
                        else:
                            with target.open("xb") as stream:
                                stream.write(content)
                    count += 1
                    total_bytes += len(content)
        if count != corpus["document_count"] or total_bytes != corpus["document_bytes"]:
            raise ValueError(f"corpus totals mismatch: {corpus['id']}")
        print(f"{corpus['id']}: {count:,} documents, {total_bytes:,} bytes verified", flush=True)


if __name__ == "__main__":
    main()
