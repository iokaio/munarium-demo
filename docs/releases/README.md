# Compatibility

All fourteen demo stacks now pin published **Munarium Server 1.2.1**. The
[1.2.1 release and integration guide](server-1.2.1.md) describes the new APIs,
image verification, existing-installation acceptance and database-restore rollback.
The [Server 1.2 guide](server-1.2.md) retains the earlier transition context.

## Versions in this checkout

| Surface | Version and integration |
|---|---|
| Root web Compose and restore-drill fallback | Server 1.2.1; custom HTTP adapter using `/v1` runbook sessions |
| Thirteen additional demo Compose stacks | Server 1.2.1, each pinned by the same immutable image digest |
| Additional demo SDK checkout | `705316332468c3c5eb50a96943f223f1bda1f09e`; latest upstream main reviewed on 2026-09-14 |
| Official Server client packages | 1.1.0, targeting Server 1.2.1 and supporting minors 1.2/1.1 |
| Rust wire crates | `munarium-api-types` and `munarium-proto` 1.2.1 |
| Inventory Matrix source | Same source checkout; Matrix and Matrix clients retain their independent 1.0.0 version |
| Web runtime/SDK | .NET 10 |
| PostgreSQL image | pgvector PostgreSQL 16 |
| Optional web Ollama baseline | 0.11.10, qwen3:1.7b and all-minilm:22m |

Server index digest:
`sha256:8c937f91b5ab952fa080bdfbc748e041fffd5b69270f5ea4052b96afdebb2df7`.
The image was built from `c638a8e56fff45cef358ff2f4a5b5ba57957ba59`;
its source identity is separate from the newer client checkout. Child manifests
and upstream acceptance are recorded in the [release guide](server-1.2.1.md#published-artifact).

The official clients are installed from a complete pinned public source checkout.
Their complete `ServerApiClient` exposes 121 named REST/native-gRPC operations;
the existing demos continue using their typed client workflows. The web app uses
its own HTTP adapter and does not import an official client package. Changing
`MUNARIUM_IMAGE` in the root stack does not change the thirteen literal Compose
pins or rebuild their SDKs. Existing explicit image overrides remain effective.

## Local 1.2.1 qualification

Qualification uses isolated `*-sdk121-review` Compose projects, the default
synthetic profile, real Server 1.2.1 and controlled keyless provider fixtures on
Windows Docker Desktop with Linux/AMD64 containers. Each application runs its
supported `local.ps1 -Action test` wrapper, including application checks,
capability boundaries, recovery checks and official SDK conformance.

All thirteen wrappers passed on **2026-09-14**, including the inventory
source-outage and Server/Matrix restart checks. Retained reports are under
`artifacts/<demo>/test/<run-id>/`; orchestration logs are under the ignored
`artifacts/server-121-upgrade/` directory. These are local working-tree runs.

| Demo | Passing run ID |
|---|---|
| employee-policy-assistant | `51dcb33c26f64a10aa93246527c3eb48` |
| engineering-change-review | `edfd5f69b7dd41e4bd5fb0895ac480ce` |
| inventory-replenishment | `3c968a566051437dbb93f75c56be65d5` |
| invoice-exception | `6edfdbbc66b74b6fa696fd36b3c04847` |
| maintenance-terminal | `29cd61386ed2432f8fecee9d7023047c` |
| master-data-reconciliation | `8e3df84061a4410eafac7cdc717f2099` |
| meeting-commitments | `5b11bbf086004a3c99abac6b78ad50aa` |
| order-exception-triage | `14158bc1af924f51b14d239f3c3f14a3` |
| policy-change-digest | `bbf9acf92e724cb89dace9d181ed6a27` |
| quality-investigation | `76018c42d30e4d3082fc1e6ee64e2e75` |
| records-intake | `57ecb9404f684f82b5b9c2094cd8c559` |
| retrieval-evaluation | `558c1be54c3349fe95431dcb37a81138` |
| shift-handover | `698b8808985a4dd59e4010d0417c6f3a` |

Inventory also passed all **20 Matrix Python SDK checks**, including its live
service test. The .NET suites passed 84 SDK tests with two documented chronology
skips per app; Java passed 65 with one documented chronology skip per app.
Rust wrappers passed their complete pinned SDK suites with no skips; the
historical kernel-chronology coverage gap remains.

The Python harness runs the current SDK unit/conformance suites plus all nine
controlled routing checks from its historical `test_server_111.py`. The shared
[release routing fixture](../../tools/test_server_routing.py) verifies Server
1.2.1 and reuses the pinned SDK's transport fixture and test functions unchanged.
Only that historical module is replaced in collection; the upstream checkout
remains unchanged, and the newer complete-API tests remain enabled. The expected
Python result is **188 passed and four documented kernel-chronology skips**.
Unexpected skips, missing tests and failures reject qualification.
The first invoice SDK run reported nine fixture errors because the historical
suite asserted exactly Server 1.1.1. Its logs are retained; the corrected routing
harness passed all nine checks in the successful run above.

The web app passed locked restore, Release build with warnings as errors,
format verification, and all five controlled regression suites: readiness,
chat context, Ollama routing, workspace/browser behavior and public configuration.
The browser checks used the repository-pinned Chromium 153.0.8010.12 (revision
1243). These web suites use controlled backends; they do not constitute a new
hosted Server 1.2.1 deployment acceptance.

These controlled runs do not establish fresh cloud-model quality, held-out/stress
capacity, native desktop behavior, native Linux/macOS or ARM64 qualification.
Dated walkthrough results and screenshots retain their original versions.
These local runs made no hosted deployment or paid-provider calls. The separate
[recorded online acceptance](#recorded-121-online-acceptance) remains below.

## Reproduce the current web default

The root Compose fallback already selects these bytes. To make an existing
installation's choice explicit, set this in its ignored `.env`:

```dotenv
MUNARIUM_IMAGE=iokaio/munarium@sha256:8c937f91b5ab952fa080bdfbc748e041fffd5b69270f5ea4052b96afdebb2df7
```

To build the same Server source locally:

```console
docker build --build-arg SOURCE_REVISION=c638a8e56fff45cef358ff2f4a5b5ba57957ba59 --build-arg BUILD_VERSION=1.2.1 -t munarium-server:source https://github.com/iokaio/munarium.git#c638a8e56fff45cef358ff2f4a5b5ba57957ba59:server
```

Set `MUNARIUM_IMAGE=munarium-server:source` before starting. A local build has
its own digest and is not the signed release artifact. Follow
[upgrade/rollback](../ops/upgrade-rollback.md) before changing an existing database.
Migration 0034 makes rollback to 1.2.0 require the pre-upgrade backup. Review
automatic vocabulary generation and provider charges before processing collections.

## Recorded 1.2.1 online acceptance

The online demo was upgraded on 2026-09-14 after a coordinated Azure database
backup and file-share snapshot. Exact REST/gRPC image checks, backend/web health
and thirteen free browser checks passed on 1.2.1, covering visitor/operator
admission, corpus pages, persona controls and responsive layouts. The web runtime
was built from merged demo source `e331bdc7fed129583d599f5d3b9db6e730873a2d`.
The existing paid-test stop remains in effect, so no fresh hosted-provider
streaming turn is claimed. The real-model container tests above are separate
from that remaining online check.

## Historical 1.2.0 web acceptance

The previous web default was `iokaio/munarium:1.2.0`, source
`d9face75f0766d93ce195f230f77d41f2ef7eec4`, index
`sha256:b1ef684bdb4d432dcb3cf750d5cd51938821232f2850496557f3fa95bc213d51`.

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
online check. The later 1.2.1 deployment is recorded separately above.

The synthetic upgrade rehearsal preserved data/configuration from 1.1.1 to
1.2.0, then restored the pre-upgrade backup and successfully started 1.1.1.
An image-only rollback cannot open the new 0032/0033 migrations. This does
not certify recovery of an arbitrary operator's database.


The previous additional-demo baseline was Server 1.1.1, index
`sha256:e19bbe4c8cb0851771d04b509be64769bb07b46c8b490b80dceabb72faa4c64f`,
and SDK checkout `bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3` (client packages 1.0.0).
Its [standalone deployment validation](open-source-preparation.md), September 11
profile measurements and cloud results remain historical evidence.
