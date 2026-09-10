# Bundled corpora

All six corpus packs are in this repository. [The inventory](../../data/README.md) lists 70,415 documents across 104 collection prefixes, with per-pack and per-document SHA-256 checksums. No downloader or private source checkout is needed to reproduce the selected input bytes.

Support, acquisition, financial advisory, and threat intelligence records are synthetic demonstration material. History contains Library of Congress material. Patents mix public patent records and generated assessments. Read [data rights](../../data/RIGHTS.md) before redistributing or adapting them; the software license does not replace source rights.

The packs preserve the deployment loader's logical filenames. History has separate core and newspaper upload passes. Support includes PDF contracts and DOCX policies to exercise extraction. Answer keys, operational inventories, cached databases, and duplicate nested patent inputs are excluded.

```console
python tools/corpora/unpack.py --verify-only
python tools/corpora/unpack.py --corpus support
python tools/corpora/loader.py --corpus support --dry-run --report-prefixes
python tools/corpora/emit_history_yaml.py --check
```

Modified extracted files are never silently overwritten. Keep your own datasets in a separate location and define their rights, layouts, shapes, and access policy deliberately. Historical quality scores mentioned in source comments are experiment records, not a guarantee for every provider or local model.
