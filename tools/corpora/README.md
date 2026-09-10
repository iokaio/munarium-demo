# Corpus tools

These standalone tools use the bundled packs and this repository's layouts. The
[source import manifest](IMPORTS.json) records original and modified checksums.
The six runbooks and six shapes are in `vendor/runbooks`; provider aliases now
match the application's `demo-*` model selectors.

| Tool | Purpose |
|---|---|
| `unpack.py` | Verify pack/member hashes and safely extract under `data/corpora` |
| `layouts.py` | Assign exact logical filenames, media types and collection prefixes |
| `loader.py` | Preflight and upload through resumable, content-addressed bulk sessions |
| `emit_history_yaml.py` | Check the 58 history shard bindings |
| `drive-run.ps1` | Manually start/resume an index run; use `-PauseEach` to inspect approvals |

Use [the setup command](../setup.py) for the supported sequence, documented in
[corpus loading and recovery](../../docs/ops/corpus-loading.md). Manual tools take
`MUNARIUM_BASE_URL`, a tenant write token in `MUNARIUM_TOKEN`, and `MUNARIUM_UID`.
Keep generated logs and run IDs private.

```console
python tools/corpora/unpack.py --verify-only
python tools/corpora/unpack.py --corpus support
python tools/corpora/loader.py --corpus support --dry-run --report-prefixes
python tools/corpora/emit_history_yaml.py --check
```

History requires both core and newspaper upload passes. Read [data rights](../../data/RIGHTS.md)
and preserve source attribution. No acquisition script or adjacent source repository
is required to reconstruct the selected document bytes.
