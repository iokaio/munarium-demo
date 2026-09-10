# Demo corpora

These packs contain the six demo corpora copied on 2026-09-09. Documents retain
their source bytes. Checksums describe the exact bundled input, independently of a particular deployment.

| Corpus ID | Documents | Runbook | Collections |
|---|---:|---|---:|
| `history` | 66,739 | `history-revolution` | 58 |
| `support` | 1,500 | `support-knowledge` | 10 |
| `dd` | 613 | `due-diligence` | 13 |
| `fin` | 1,233 | `financial-advisory` | 14 |
| `patents` | 266 | `patent-analysis` | 5 |
| `intel` | 64 | `threat-intelligence` | 4 |

[manifest.json](manifest.json) records the source revision, counts, sizes, and
archive checksums. Each ZIP includes `_manifest.json`, with a SHA-256 checksum,
media type, and logical upload path for every document. History is divided by
source collection and size to keep individual archives small. Other corpora have one pack
each. The archive member manifests are packaging metadata and are never ingested.

There are 30 packs totaling 269,655,924 bytes (about 257 MiB compressed), each
smaller than 50 MiB. The unpacked documents total 561,794,453 bytes (about 536 MiB).

The selection follows the existing deployment loader. It excludes answer keys,
research results, source manifests, cached downloads, SQLite indexes, and the
duplicate nested patent corpus. The data-room index and advisory corpus index
are intentional source documents and remain included.

## Verify and unpack

Use Python 3.11 or newer, from the repository root. These commands need no
network, API key, Azure account, or sibling checkout.

```console
python tools/corpora/unpack.py --verify-only
python tools/corpora/unpack.py
python tools/corpora/loader.py --corpus dd --dry-run --report-prefixes
python tools/corpora/emit_history_yaml.py --check
```

Use `--corpus support` with `unpack.py` to extract just one corpus. Extracted
files go into `data/corpora/`, which is ignored by Git and Docker. Existing files
are accepted only if their hashes match; changed documents are never overwritten.
The history upload is two passes: `--corpus history` and
`--corpus history-newspapers`. Upload both before starting its runbook.

## Provenance and publication review

Support, data-room, advisory, and threat-intelligence documents are described by
their source projects as synthetic. History consists of Library of Congress
material. Patents combine public USPTO records and generated assessments;
generated assessments are demonstration artifacts, not official examiner opinions.

[RIGHTS.md](RIGHTS.md) describes synthetic corpus licensing and original-source rights.
Preserve embedded attribution and source identifiers. The root Apache-2.0 software
license does not relicense third-party records.

See [corpus loading](../docs/ops/corpus-loading.md) for the standalone setup and recovery procedure.
