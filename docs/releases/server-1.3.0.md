# Server 1.3.0 demo upgrade

All fourteen demo stacks pin the published **Munarium Server 1.3.0** image.
The root web stack and restore drill use the same digest by default; an existing
`MUNARIUM_IMAGE` override still takes precedence. Each additional application has
its own literal Compose pin and requires 1.3.0 during bootstrap. Inventory also
sets Matrix's target Server version to 1.3.0.

## Published artifact

The [upstream release](https://github.com/iokaio/munarium/releases/tag/v1.3.0)
records these identities. The tag's OCI index and platform manifests were also
checked with `docker buildx imagetools inspect iokaio/munarium:1.3.0`.

| Artifact | Identity |
|---|---|
| Source | `eaa04ac6da25cb332b674c6535013a19b87fa0e7` |
| OCI index | `sha256:55078aa474c214dd68d84bfe92d7c6ce2d696fad0f36cc5dd270b160d8c1ec4f` |
| Linux AMD64 | `sha256:b88fe105781bb1ac477f91b7767b9c38d7ae1bca29c1150e6cc9590addca573e` |
| Linux ARM64 | `sha256:540da00b6f87a27dea2c88e9ef231e59e494fc29e63fbff74f933798b29724db` |

The demos build Server client source packages **1.2.0**, Rust wire crates
**1.3.0**, and Matrix client source **1.1.1** from the same public release checkout
`eaa04ac6da25cb332b674c6535013a19b87fa0e7`. Matrix itself remains **1.0.0**.
These are pinned source builds, not claims about package registry publication.
The web app keeps its own HTTP adapter.

Server 1.3.0 supplies model evidence as JSON envelopes. Canned provider fixtures
now decode document text, ledger sections and sealed table rows from those
envelopes and retain their supplied citation identifiers. The invoice, employee
policy, meeting, quality and inventory prompts describe the new citation format.
Inventory and quality investigation resolve document citations by the returned
collection and chunk ID rather than substituting a hierarchy layer name. Source
scope, hash validation, fault modes and oracle separation remain enforced. Existing demo
namespaces incorporate changed runbooks or source revisions during bootstrap;
do not reuse an interrupted run's journal against newly provisioned state.

## Web stack upgrade and acceptance

Review the [upstream upgrade guide](https://github.com/iokaio/munarium/blob/eaa04ac6da25cb332b674c6535013a19b87fa0e7/server/docs/guides/server-1.3.md)
before upgrading an existing installation. Server 1.3 adds durable governance
profiles, guarded commands, runbook checkpoints, source holds/denials, and token
accounting changes. This image update does not add UI or SDK workflows for those
features or opt demo collections into new governance policies.

1. Record the running image, configuration, runbook versions and durable work.
   Back up PostgreSQL, sources and artifacts, and preserve authoritative recovery
   and retention records. Drain every relevant process before upgrading.
2. Rehearse against an isolated restored database. Startup applies migrations
   0035–0040 after the 1.2.1 schema. Review provider settings, vocabulary generation
   and budgets before allowing model calls.
3. Preserve existing private settings. To select the new image explicitly in an
   existing root stack's ignored `.env`, set:

   ```dotenv
   MUNARIUM_IMAGE=iokaio/munarium@sha256:55078aa474c214dd68d84bfe92d7c6ce2d696fad0f36cc5dd270b160d8c1ec4f
   ```

4. Recreate Server, verify `/version` reports 1.3.0 and wait for readiness. Check
   active indexes, representative retrieval/chat, persona boundaries and visitor
   admission using the [quickstart checks](../guides/quickstart.md). Readiness
   alone does not qualify retrieval or completion.

An older binary cannot open the migrated schema. Before policy activation or
external effects, rollback requires the matching pre-upgrade backup and image.
After activation or external effects, that backup alone is insufficient: keep
restores isolated until authoritative recovery and retention records are
reconciled, and prefer a compatible roll-forward fix. An image-only downgrade is
not a rollback. See [backup/restore](../ops/backup-restore.md).

## Local validation

All thirteen applications passed their complete `local.ps1 -Action test`
wrappers on **2026-09-26**, using isolated projects, the default synthetic
profile, real Server 1.3.0 and keyless controlled provider fixtures. These were
working-tree runs on Windows Docker Desktop with Linux/AMD64 containers.
Each wrapper retained its capacity preflight, application checks, recovery
checks and official SDK conformance. Inventory also passed source-outage and
Server/Matrix restart checks and all 20 Matrix Python SDK tests.

| Demo | Passing run ID |
|---|---|
| employee-policy-assistant | `440c970a10e7425e886fabdb57157480` |
| engineering-change-review | `8b76f2f1d94d4e95b6e73fa1f31a3694` |
| inventory-replenishment | `79504d52969e4f618d31a16253c5d724` |
| invoice-exception | `eb80242a5a0d485590075a3e9e865579` |
| maintenance-terminal | `09030b4171f14330893a1af25a324c4a` |
| master-data-reconciliation | `6833146a6aed434a897b45262148d9f7` |
| meeting-commitments | `fb0dd71c36c843388be04f98b78b2341` |
| order-exception-triage | `714d6800553c4b4f96f6760577d5e7a4` |
| policy-change-digest | `7ff9260f752e480b825c40bb01113e42` |
| quality-investigation | `e9b4c6c31cbc4d3bb790f19edbb82ed1` |
| records-intake | `856258642c604cd8a278d3f9af62edea` |
| retrieval-evaluation | `4df653e04ba543cd8e2e3ccb6b4c3e23` |
| shift-handover | `da7f720ab0d146e4a298aa5bc4517fa0` |

Reports remain under ignored `artifacts/<demo>/test/<run-id>/`; wrapper logs,
including earlier failed runs, are under `artifacts/server-130-upgrade/`.
Test stacks were stopped, with reports and volumes retained. The Python SDK
suites passed 231 tests with four documented chronology skips per app; .NET
passed 110 with two documented chronology skips; Java passed 102 with one
documented chronology skip. Rust SDK suites passed with no skips; their existing
kernel-chronology coverage gap remains.

Additional local checks passed:

- `dotnet restore Demo.sln --locked-mode`, Release build with warnings as errors,
  and `dotnet format Demo.sln --verify-no-changes --no-restore`.
- Web readiness, chat-context, Ollama and public-configuration regression suites.
- `python tools/test_provider_evidence.py`: four focused tests for JSON framing,
  escaped document text, citation identity, fault behavior and table column order.
- All fourteen Compose configurations, registry manifest identities, public and
  license scans, documentation links, bundled asset verification, CI boundary
  self-tests and checks, inventory wrapper syntax, and `git diff --check`.

The workspace browser suite could not run: the pinned Playwright Chromium was
absent and its download timed out. PSScriptAnalyzer 1.24.0 was not installed;
PowerShell parser validation and the updated inventory wrapper itself passed.
Automatic CI remains enabled and provides its configured gates.

These checks do not establish new cloud-model quality, stress capacity, native
desktop behavior, native Linux/macOS or ARM64 qualification, hosted deployment
acceptance, or restoration of an existing operator database. No deployment or
paid-provider call was made. The [September 14 qualification](README.md#local-121-qualification)
remains historical Server 1.2.1 evidence. Dated walkthrough screenshots, stress
results, native desktop checks and online runs retain their original baselines.
