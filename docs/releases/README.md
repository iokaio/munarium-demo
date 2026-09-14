# Compatibility

The [Server 1.2 upgrade guide](server-1.2.md) covers the complete API/client
surface, vocabulary defaults, original-file references and database restore
rollback. The default web-demo deployment now pins the published 1.2.0 image.

| Component | Web-demo baseline |
|---|---|
| Munarium Server | `iokaio/munarium:1.2.0` |
| Server source | `d9face75f0766d93ce195f230f77d41f2ef7eec4` |
| Server index digest | `sha256:b1ef684bdb4d432dcb3cf750d5cd51938821232f2850496557f3fa95bc213d51` |
| Web runtime/SDK | .NET 10 |
| PostgreSQL image | pgvector PostgreSQL 16 |
| Optional local Ollama baseline | 0.11.10, qwen3:1.7b and all-minilm:22m |

Server 1.2.0 was published on **2026-09-14**. The exact signed AMD64 and ARM64
manifests passed public-pull, real Ollama REST/gRPC, retrieval and restart
persistence checks. AMD64 ran natively; ARM64 ran under emulation. The
version-aware test harness is `2aee87b1c643063513c98a25c4e95476ffa8a152`.
Image audits, security scans and main-branch source CI passed. See the
[public release](https://github.com/iokaio/munarium/releases/tag/v1.2.0) for
child digests, signature verification and the exact scope of qualification.

The online web demo was upgraded to this backend on 2026-09-14. Deployment
checks confirmed the exact image on both REST and gRPC services and passed
web health checks. Thirteen browser checks passed for visitor/operator gates,
persona controls and responsive layouts. These were free UI checks; a fresh
hosted-provider streaming answer was not completed in this acceptance run.
The isolated real-model container tests above are separate from that remaining
online check. Server 1.2.1 is not yet a qualified or published baseline.

The synthetic upgrade rehearsal preserved data/configuration from 1.1.1 to
1.2.0, then restored the pre-upgrade backup and successfully started 1.1.1.
An image-only rollback cannot open the new 0032/0033 migrations. This does
not certify recovery of an arbitrary operator's database.

The web app is built from this repository and uses its own HTTP adapter.
Additional example applications keep their individually pinned Server and SDK
versions until their own wrappers and acceptance suites are updated. This is
not a release of all examples, Matrix or every client under one version.
Server client source packages 1.1.0 provide the complete 1.2 API; they remain
source-installed rather than published to language package registries.

To pin these exact bytes, set the following in your ignored `.env`:

```dotenv
MUNARIUM_IMAGE=iokaio/munarium@sha256:b1ef684bdb4d432dcb3cf750d5cd51938821232f2850496557f3fa95bc213d51
```

To build the same Server source locally:

```console
docker build --build-arg SOURCE_REVISION=d9face75f0766d93ce195f230f77d41f2ef7eec4 --build-arg BUILD_VERSION=1.2.0 -t munarium-server:source https://github.com/iokaio/munarium.git#d9face75f0766d93ce195f230f77d41f2ef7eec4:server
```

Set `MUNARIUM_IMAGE=munarium-server:source` before starting. A local build has
its own digest and is not the signed release artifact. Follow
[upgrade/rollback](../ops/upgrade-rollback.md) for changes to an existing installation.
Review automatic vocabulary generation and configured model charges before
the upgraded Server starts processing existing collections.

The previous 1.1.1 baseline was source
`91c34b1b2a416cfa5e504b5bfe945be89f6ace89`, index
`sha256:e19bbe4c8cb0851771d04b509be64769bb07b46c8b490b80dceabb72faa4c64f`.
Its historical [standalone deployment validation](open-source-preparation.md)
remains a separate record; it is not relabeled as a new 1.2 execution.
