# Munarium Server 1.2.1: release and demo integration

Munarium Server **1.2.1 was published on 2026-09-14**. The
[Server publication record](https://github.com/iokaio/munarium/blob/main/server/CONTAINER.md#versions-and-verification)
records the signed image, verification and rollback results. This checkout's
web Compose stack and thirteen additional demos all pin **1.2.1**. The
[compatibility record](README.md) records the current client source and local
qualification separately from historical online acceptance.

## Published artifact

| Artifact | Identity |
|---|---|
| Release | [Server 1.2.1](https://github.com/iokaio/munarium/releases/tag/v1.2.1) |
| Image and acceptance-suite source | `c638a8e56fff45cef358ff2f4a5b5ba57957ba59` |
| OCI index | `sha256:8c937f91b5ab952fa080bdfbc748e041fffd5b69270f5ea4052b96afdebb2df7` |
| AMD64 manifest | `sha256:2515c419524974ca02db10a79631ac75bc451e3253047620b9cacbff69a51141` |
| ARM64 manifest | `sha256:b5f53e3d0f55598d7a99cbeaa0ce0e068bc40863bd2e50a0cd49ab641b71c760` |

The upstream record reports public-pull runtime, authentication, PostgreSQL,
CLI, restart, and real Ollama REST/gRPC completion, embedding and retrieval
checks. AMD64 ran natively and ARM64 under emulation. It also records a
1.2.0 → 1.2.1 upgrade followed by restoration of the pre-upgrade database and
a successful restart on 1.2.0. Those are Server release results; they do not
establish online-demo acceptance. Local application qualification is recorded
separately in [compatibility](README.md).

## What the existing demo uses

The web backend's own [HTTP adapter](../../src/Demo.Web/Services/MunariumClient.cs)
mints `query` capabilities, creates runbook sessions, and submits `/v1` turns.
Search is a session turn with `complete:false`; chat uses completing turns,
including REST server-sent events. The selected chat model controls expansion
and completion on Server 1.1.1 and later. Without an override, each task uses
its runbook model. A Server upgrade preserves this session API; it does not
switch the application to `/v1.2/query` or add a vocabulary/governance editor.

Session retrieval can apply the collection vocabulary on Server 1.2. Model
budgets and routing still follow the session runbook. The web DTOs consume
session hits and completion text, not the new `content.answer`, publication
governance or extraction-location response fields.

The additional applications fetch official clients from
`705316332468c3c5eb50a96943f223f1bda1f09e` (client packages 1.1.0).
These clients target Server 1.2.1 and support
Server minors 1.2/1.1. Their `ServerApiClient` exposes 121 named operations on
REST and native gRPC. Installing an image does not update a pinned SDK source
checkout. Requalify each application's wrapper and tests when changing its pins;
Matrix's independent version and inventory integration need their own checks.

## Integrating collection queries

For an application that adopts the new API, follow the
[Server collection guide](https://github.com/iokaio/munarium/blob/main/server/docs/guides/collection-vocabularies.md)
and [complete SDK guide](https://github.com/iokaio/munarium/blob/main/clients/docs/guides/server-1.2.md):

- `POST /v1.2/query` takes `question`, `collections` and optional `effective_on`.
  Server selects publications, applies vocabulary, retrieves and composes the
  answer. Callers do not send passages, index pins, model overrides or prompts.
  The date defaults to today in UTC; past dates require historical clearance,
  and future dates are rejected.
- Governance GET/PUT requires static `rw` credentials. PUT replaces the complete
  snapshot using its current revision and returns `409 head-conflict` for stale
  writes. Retain publication identities and withdrawal tombstones. Migration
  0034 stores append-only snapshots. Rebuilding may change an index pin while
  preserving the publication's source identity and bytes.
- Without a stored snapshot, queries search the collection's own active index.
  Once saved, the snapshot's publications define the scope; an empty list
  supplies no files. Existing session runbooks are not governance snapshots.
- A `query` capability grants collection queries and publication authorization.
  Vocabulary reads, edits and refresh use the separate `vocabulary` scope;
  query callers can read the vocabulary revision but not its terms. Keep these
  credentials in the backend and preserve the visitor's uid and clearance.
- Display `content.answer`, including explanations for `insufficient` and
  `review`, with verified references. The query response also returns
  `effective_on`, `governance_revisions` and `vocabulary_revisions`, with revision
  maps keyed by parent collection ID. A citation check establishes its source
  and exact quotation, not the correctness of every narrative claim.
- References identify internal source collections/indexes and source hashes;
  they do not include parent collection or publication IDs. Retain the publisher's
  mapping to those IDs. Before serving a retained original, call
  `GET /v1.2/collections/{id}/publications/{publication_id}` and also enforce
  current application admission and file permissions. Server returns a
  publication identity, never file bytes or a download grant.

Collection queries default to external processing disabled. Publishers configure
provider/tier and budgets, with optional exact-clearance `query.model_routes`.
These do not replace session runbook settings. Vocabulary generation uses a
stored collection's base model/processing policy and `complete_default` output
budget; without stored governance it uses tenant vocabulary settings. Automatic
generation is enabled by default, including for eligible existing collections.
Review sampling and model charges before upgrading a credentialed installation.

Answer and vocabulary protocols request provider-native structured output;
select a model/endpoint supporting it. Ordinary session/provider completion
retains its response format. OpenRouter's optional `spec.openrouterProvider`
selects one downstream for completion, disables fallback, requires parameter
support and requests `data_collection: deny`; it does not independently
guarantee a provider's retention practices.

## Web-stack upgrade and acceptance

1. Record deployed image identities, configuration, active indexes and source
   mappings. Back up PostgreSQL, any external source/artifact stores, and visitor
   SQLite state with matching secrets. Rehearse a restore in isolation first.
2. Use the upstream publication record to verify the release signature and
   exact digest. To select it for the web stack, set this in the existing ignored
   `.env` without replacing the file or its secrets:

   ```dotenv
   MUNARIUM_IMAGE=iokaio/munarium@sha256:8c937f91b5ab952fa080bdfbc748e041fffd5b69270f5ea4052b96afdebb2df7
   ```

   Use the same digest on separately deployed REST/gRPC services. The thirteen
   per-demo Compose files have their own literal image pins and do not inherit
   this root override.
3. Review vocabulary generation and provider settings, recreate Server in an
   isolated restored deployment, and confirm `/version` reports `1.2.1`.
   Migration 0034 runs on startup; upgrades from 1.1 also apply 0032/0033.
4. Verify active indexes and counts, visitor/operator admission, all six
   workspaces, persona restrictions, actual streaming answers, citations, and
   restart persistence. Readiness and free browser checks alone do not establish
   completion quality. Record image identities, test scope and model-dependent
   limits before accepting a deployment.

**Rollback from 1.2.1 to 1.2.0 requires the pre-0034 database backup.** Stop
writes, preserve the failed deployment's database for reconciliation, restore
the matching backup and configuration, then start the older image and verify
the application. Do not delete migration-history rows or expect an older binary
to open the upgraded database. Restoring a backup loses later writes.

The [restore drill](../ops/backup-restore.md) reads `MUNARIUM_IMAGE` from its
settings and otherwise defaults to 1.2.1; it does not detect the running Server
image. Keep that setting synchronized with the database being restored. A drill
of an already upgraded database is distinct from proving rollback to a backup
made before migration 0034. See [upgrade/rollback](../ops/upgrade-rollback.md).
