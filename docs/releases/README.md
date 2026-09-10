# Compatibility

See [standalone deployment validation](open-source-preparation.md) for the bundled-data acceptance results.

| Component | Demo baseline |
|---|---|
| Munarium Server | `iokaio/munarium:1.1.1` |
| Server source | `91c34b1b2a416cfa5e504b5bfe945be89f6ace89` |
| Server index digest | `sha256:e19bbe4c8cb0851771d04b509be64769bb07b46c8b490b80dceabb72faa4c64f` |
| Web runtime/SDK | .NET 10 |
| PostgreSQL image | pgvector PostgreSQL 16 |
| Ollama | 0.11.10, qwen3:1.7b and all-minilm:22m |

Server 1.1.1 was published and anonymously pulled successfully on 2026-09-10 UTC. Its signed AMD64/ARM64 images passed pulled-image Ollama and persistence checks; ARM64 ran under emulation.

To build the compatible Server directly from public source:

```console
docker build --build-arg SOURCE_REVISION=91c34b1b2a416cfa5e504b5bfe945be89f6ace89 -t munarium-server:source https://github.com/iokaio/munarium.git#91c34b1b2a416cfa5e504b5bfe945be89f6ace89:server
```

Set `MUNARIUM_IMAGE=munarium-server:source` in your ignored `.env` before starting. A local build has its own digest and is not the signed release artifact. See [upgrade/rollback](../ops/upgrade-rollback.md) for deployment changes.
