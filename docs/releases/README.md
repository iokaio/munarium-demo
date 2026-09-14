# Compatibility

The [Server 1.2 upgrade guide](server-1.2.md) covers the API/client surface,
vocabulary defaults and original-file references. The default web-demo deployment
pins published Server 1.2.1. The [1.2.1 transition guide](server-1.2.1-candidate.md)
describes Server-owned collection queries, explanatory answers and upgrade checks.

| Component | Web-demo baseline |
|---|---|
| Munarium Server | `iokaio/munarium:1.2.1` |
| Server source | `c638a8e56fff45cef358ff2f4a5b5ba57957ba59` |
| Server index digest | `sha256:8c937f91b5ab952fa080bdfbc748e041fffd5b69270f5ea4052b96afdebb2df7` |
| Web runtime/SDK | .NET 10 |
| PostgreSQL image | pgvector PostgreSQL 16 |
| Optional local Ollama baseline | 0.11.10, qwen3:1.7b and all-minilm:22m |

Server 1.2.1 was published on **2026-09-14**. The exact signed AMD64 and ARM64
manifests passed public-pull, real Ollama REST/gRPC, retrieval and restart
persistence checks. AMD64 ran natively; ARM64 ran under emulation. Image audits,
security scans and merged-source CI passed. See the
[public release](https://github.com/iokaio/munarium/releases/tag/v1.2.1) for
child digests, signature verification and qualification scope.

A synthetic upgrade rehearsal preserved data/configuration from 1.2.0 to 1.2.1,
then restored the pre-upgrade backup and successfully started 1.2.0. An image-only
rollback cannot open migration 0034. This does not certify recovery of an
arbitrary operator's database. Preserve a coordinated database and file-store
backup before deployment.

The online deployment is qualified separately from container publication.
Its previous 1.2.0 deployment passed exact REST/gRPC image checks and thirteen
free browser checks; no fresh hosted-provider streaming turn was completed in
that run. Do not treat those historical checks as 1.2.1 live acceptance.

The web app is built from this repository and uses its own HTTP adapter.
Additional example applications keep their individually pinned Server and SDK
versions until their own wrappers and acceptance suites are updated. This is
not a release of all examples, Matrix or every client under one version.
Server client source packages 1.1.0 provide the complete 1.2 API; they remain
source-installed rather than published to language package registries.

To pin these exact bytes, set the following in your ignored `.env`:

```dotenv
MUNARIUM_IMAGE=iokaio/munarium@sha256:8c937f91b5ab952fa080bdfbc748e041fffd5b69270f5ea4052b96afdebb2df7
```

To build the same Server source locally:

```console
docker build --build-arg SOURCE_REVISION=c638a8e56fff45cef358ff2f4a5b5ba57957ba59 --build-arg BUILD_VERSION=1.2.1 -t munarium-server:source https://github.com/iokaio/munarium.git#c638a8e56fff45cef358ff2f4a5b5ba57957ba59:server
```

Set `MUNARIUM_IMAGE=munarium-server:source` before starting. A local build has
its own digest and is not the signed release artifact. Follow
[upgrade/rollback](../ops/upgrade-rollback.md) for an existing installation.
Review automatic vocabulary generation, sample settings and configured model
charges before processing existing collections. Review generated terms for
semantic accuracy; administrators can edit or disable each vocabulary.

The previous [1.2.0 release](https://github.com/iokaio/munarium/releases/tag/v1.2.0)
remains pinned at `sha256:b1ef684bdb4d432dcb3cf750d5cd51938821232f2850496557f3fa95bc213d51`.
The older [standalone deployment validation](open-source-preparation.md) remains
a historical record and is not relabeled as a new 1.2.1 execution.
