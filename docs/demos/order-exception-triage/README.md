# Order exception triage

## Business case

A distributor or retailer can accumulate order holds caused by stock shortages, address problems, missing export documents or substitution requests. Staff must identify the right team and consult the relevant procedure before resolving each hold, and duplicate events can create repeated work. A similar consumer could turn those events into durable, cited review packets and route proposed work consistently. The business value is a clearer exception queue, fewer repeated investigations and traceable handoffs between fulfillment, procurement and customer service. People and existing order systems retain control of shipment and cancellation; the demo's synthetic results are not a measured throughput improvement.

## Application

This Java 21 queue consumer reads fictional order-hold events from a local inbox, asks Munarium Server for a cited explanation and proposed team, and writes a review packet. H2 persists input hashes, session IDs, submission state, saved responses, and an outbox. Two bounded workers process independent events. Duplicate delivery reuses the saved packet; an uncertain turn stays pending until explicit transcript reconciliation. Order cancellation, shipment, and ERP updates belong to future adapters.

![Order consumer terminal and persisted review packet](application.png)

The image is a Java2D rendering of actual controlled-test output, including the saved explanation. It is a terminal/output composition, not an operating-system screenshot. All inputs are fictional and the controlled provider returns canned responses without local inference.

Source and harness: [src/order-exception-triage](../../../src/order-exception-triage). This demo owns its Compose project and generated state; the original web demo is outside its workflow.

## Run locally

Use Docker Desktop with Linux containers on Windows or macOS, or Docker Engine with Compose on Linux, plus Git. The image contains Java 21, Gradle, generators, and tests; no host JDK is needed. The full official Java client source is fetched at commit `bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3`, including the Server protobuf definitions. Server 1.1.1, PostgreSQL/pgvector, and the Java base image are pinned by digest. Gradle resolves the application dependency graph using [gradle.lockfile](../../../src/order-exception-triage/gradle.lockfile).

From the repository root:

| Action | PowerShell | POSIX shell |
|---|---|---|
| Complete controlled suite | `./src/order-exception-triage/local.ps1 -Action test` | `sh src/order-exception-triage/local.sh test` |
| Three online providers | `./src/order-exception-triage/local.ps1 -Action cloud` | `sh src/order-exception-triage/local.sh cloud` |
| Stop project containers, retain state | `./src/order-exception-triage/local.ps1 -Action stop` | `sh src/order-exception-triage/local.sh stop` |

The default project is `orders-wave1`; supply `-Project orders-cleanroom` or the second POSIX argument `orders-cleanroom` for fresh volumes. The wrappers verify a local Linux Docker context, inventory existing containers, build the runner, and save host/image identity and the invocation. No host ports or GPU are required. Initial builds download dependencies; tests invoke Gradle offline afterward. The fixture generator has no network and receives a private oracle volume separate from the document volume. Server and the consumer never mount the oracle.

The keyless `test` action runs unit checks, bootstrap and all eight business cases, access and expiry tests, provider outage and dropped-stream recovery, duplicate delivery, a worker process crash, and recovery after restarting only this project's Server. It then runs the Java SDK build, unit tests, and REST/gRPC conformance serially. It also renders the example packet. Unexpected skips, empty test reports, failed assertions, or unresolved acceptance events fail the command.

The `cloud` action requires the existing ignored repository-root `.env.local`. If absent, copy [`.env.local.sample`](../../../.env.local.sample) only when the destination does not exist, then fill `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, and `OPENROUTER_API_KEY`. Preserve existing values. Keys are passed only to Server, where named `credentialRef` entries resolve them. The preferred model variables are passed to bootstrap. Never print expanded Compose configuration or commit credentials.

Real AI testing uses only OpenAI, Anthropic, and OpenRouter. Each invocation creates a fresh run directory, attempts every provider, and fails overall if any assigned case fails. It never falls back to local Ollama. The keyless provider fixture implements canned Ollama-protocol responses for deterministic integration tests; it starts no Ollama process and downloads no completion models.

The cloud harness waits 30 seconds before each independent OpenRouter case to reduce upstream bursts. Override `ORDER_OPENROUTER_CASE_DELAY_SECONDS` with a value from 0 to 60 in the invocation environment if needed. This paces new cases; it never retries a submitted turn or converts a rate-limit failure into a pass.

## Business cases and acceptance

The seeded Java generator creates eight event JSON records and eight scope-specific procedure documents, plus a separate declarative oracle and SHA-256 manifest. Repeated generation with the same seed produces identical bytes. Documents include Unicode, a fixed logical date, and fictional organization names. The oracle is never ingested.

| Cases | Hold | Expected proposed team and behavior | Online assignment |
|---|---|---|---|
| `event-001` | Partial stock shortage | PROCUREMENT; hold and replenishment review | OpenAI `gpt-5.4-mini` |
| `event-002` | Invalid address | CUSTOMER_SERVICE; request address confirmation | OpenAI `gpt-5.4-mini` |
| `event-003` | Substitution without consent | FULFILLMENT; explain the consent requirement | Anthropic `claude-haiku-4-5-20251001` |
| `event-004` | Missing export documents | COMPLIANCE; explicitly disclose missing evidence | Anthropic `claude-haiku-4-5-20251001` |
| `event-005` | Carrier delay | LOGISTICS; carrier status review | OpenRouter `qwen/qwen3.8-flash` |
| `event-006` | Unknown hold | MANUAL_REVIEW; insufficient evidence | OpenRouter `qwen/qwen3.8-flash` |
| `event-007` | No available stock | PROCUREMENT; hold and replenishment review | OpenAI `gpt-5.4-mini` |
| `event-008` | Substitution with consent | FULFILLMENT; consent permits review, not shipment | Anthropic `claude-haiku-4-5-20251001` |

The live workload is balanced 3/3/2 across OpenAI, Anthropic, and OpenRouter to reduce OpenRouter rate-limit exposure while preserving all eight business scenarios and at least two cases per provider.

Application validation requires a declared routing enum, `review_required` disposition, a boolean missing-evidence field, a nonempty explanation, and citations resolving to actual hits in the event's collection. Packets retain source identity, path, content hash, collection provenance, actual model identity, usage, and Server verification results. The independent acceptance suite additionally checks the expected route, missing-evidence outcome, and scenario-specific explanation terms. These bounded checks demonstrate the fictional cases; they are not a general model-quality benchmark.

## Client walkthrough and recovery

[Bootstrap.java](../../../src/order-exception-triage/src/main/java/io/ioka/demo/orders/Bootstrap.java) uses the official Java SDK to apply a named provider, check its health, apply a shape and per-event runbooks, ingest documents, run indexing, inspect each pending cutover, and approve only the intended collection in the recorded run. A separate management client mints a short-lived query capability with bare runbook metadata names; session creation pins `name@1` and uses the matching uid. The app receives that query grant, not management or provider credentials.

[Worker.java](../../../src/order-exception-triage/src/main/java/io/ioka/demo/orders/Worker.java) calls `sessions.create`, then saves the event hash, query, and session with state `uncertain` before `sessions.turnStream`. The runbook selects a named completion provider with the fast tier and a 768-token answer budget. Query expansion is disabled, so there is no separate paid expansion stage. Returned progress, completion usage, and verification metadata are saved before validating the packet. Completed state and the outbox record are committed together; exports are atomic and can be regenerated from the database.

The review outbox is an application-owned record of proposed work awaiting human review. An event marked `complete` means its packet has been produced; it does not mean the underlying order was fulfilled. H2's embedded database lock permits one consumer process per inbox, with two bounded worker threads inside it. A broker adapter would acknowledge delivery only after durable inbox acceptance. Reusing an event ID with changed facts is rejected. Queue delivery deduplication does not provide exactly-once execution across Server or external ERP systems.

Inspect and reconcile a saved run from `src/order-exception-triage/`, replacing the example path with the recorded run directory:

```sh
docker compose --env-file ../../.env.local.sample -p orders-wave1 run --rm --no-deps app inspect /work/test/RUN/restart
docker compose --env-file ../../.env.local.sample -p orders-wave1 run --rm --no-deps app reconcile /work/test/RUN/restart
```

The same commands work in PowerShell. Bootstrap credentials must match the original provider/configuration before reopening an inbox. If its capability expires, rerun bootstrap for that same provider and model to renew the grant while preserving the uid and runbook revision. Do not replace the inbox with a fresh one to retry an uncertain paid turn.

Reconciliation reads the saved session through `sessions.get`. Exactly one matching completed turn with the expected uid, runbook, and resolved provider can restore a packet. Empty or ambiguous transcripts stay uncertain, and the consumer never resubmits them automatically. Recovery preserves stored `completion.resolved` metadata and marks recovered evidence; it omits unavailable live skipped-collection/progress fields. Provider outages, auth errors after submission, and invalid output remain visible for review rather than being silently retried.

## Reports and rendering

Each invocation writes under ignored `artifacts/order-exception-triage/test/<run>/` or `cloud/<run>/`. Cloud provider subdirectories contain individual case inboxes, packets, `quality.json`, Gradle logs, JUnit XML, and summaries. Failed and interrupted runs remain available. Bootstrap stores fixture manifests, source revision, provider/model identity, and index run IDs under `artifacts/order-exception-triage/bootstrap/`. No wrapper resets volumes or cleans unrelated resources.

The controlled wrapper creates `<run>/application.png`. To reproduce it explicitly from `src/order-exception-triage/`:

```sh
docker compose --env-file ../../.env.local.sample -p orders-wave1 run --rm --no-deps app render /work/test/RUN/controlled/event-001/packets /work/test/RUN/application.png
```

Copy the generated image into this documentation folder after inspecting it. Rendering uses Java2D and container DejaVu fonts, not an AI image generator. Runtime dependencies remain under their upstream licenses and retain packaged notices; the official client carries its LICENSE and NOTICE. The synthetic material and application rendering are original Apache-2.0 demo assets. Preserve the Java runtime and dependency notices when distributing the generated application.

## Recorded validation

On 2026-09-10 (America/Denver; reports use UTC), `./src/order-exception-triage/local.ps1 -Action test -Project orders-final` passed from fresh project volumes. Reports: `artifacts/order-exception-triage/test/92902aacab2841608b0e0e3d4f3e11a0/`.

| Coverage | Recorded result |
|---|---|
| Application unit checks | 9 passed, including byte-for-byte generation in separate JVM processes |
| Real-Server controlled tests | 19 passed, including all eight business cases and failure/recovery checks |
| Recovery after dedicated Server restart | 1 passed, with one stored completed turn and no resubmission |
| Official Java SDK build, unit, REST/gRPC conformance | 63 passed; 1 documented kernel-only chronology skip |
| Rendering | Generated from the persisted controlled packet and visually inspected |

The exact permitted SDK skip is `ScenariosTest.gatesChronologyCertainOnly()`: the upstream scenario tests kernel chronology rules without a corresponding client API. Report validation rejects all other skips and never counts this one as passed. The POSIX wrapper also completed successfully in Git Bash on Windows with fresh `orders-cleanroom-final` volumes, recorded under `test/20260911T014440Z-a57ed9078427d0f9/`; that run preceded the additional cross-JVM fixture regression. Git Bash invocation used `MSYS_NO_PATHCONV=1` to preserve container paths. PowerShell parsing, POSIX shell syntax, documentation links, public-material checks, license checks, and the ignored key-file/tracked empty-sample checks passed.

Initial harness failures exposed an uncached JUnit runtime dependency, a test that omitted Server's 30-second JWT clock-skew allowance, and an incorrect expected SDK skip count. These were corrected and the final controlled invocation exited successfully. Preserve earlier reports as diagnostic history.

Two earlier fresh online runs demonstrated upstream OpenRouter rate limits: `cloud/9b17ab9eff284ea090e7c7b008191d61/` passed 6 of 8 cases, with `event-007` and `event-008` returning HTTP 429; `cloud/7425c125b1de417d92f4395d7b40739e/` passed 7 of 8, with `event-006` returning HTTP 429. All OpenAI and Anthropic cases passed in both runs. The failed OpenRouter sessions had no recoverable completed transcript and remained uncertain after explicit reconciliation. Both cloud commands correctly exited nonzero; their journals and failure reports remain intact, and results from separate runs are not combined into a passing qualification.

The initial successful paced online qualification, before rebalancing to 3/3/2, is recorded under `cloud/829595bb09684a86b2694bc2f2b8bd1d/`, using `ORDER_OPENROUTER_CASE_DELAY_SECONDS=60` with `./src/order-exception-triage/local.ps1 -Action cloud -Project orders-cloud-wave1`. All eight fresh cases passed with no skips or uncertain outputs: two on OpenAI, two on Anthropic, and four on OpenRouter. The command exited successfully. Pacing does not guarantee future upstream availability; preserve any later failures separately. The final controlled and cloud generator volumes produced the identical manifest SHA-256 `8fe2ccde75c0d23559d9c43bdc7cf061fa41602e70ef735307f8fbd96cf844eb`.

The rebalanced suite passed all eight fresh cases under `cloud/62cc7732a7374fc6880125604fd33f2f/`: three OpenAI, three Anthropic, and two OpenRouter, with no skips or uncertain outputs. The cloud wrapper also passed all nine unit checks and exited successfully. It used the same preferred models and a 60-second OpenRouter case delay. All original scenarios remain covered; OpenRouter receives half as many cases as in the initial distribution.

Host evidence establishes Docker Desktop on Windows with Linux/AMD64 containers. Docker had 12 CPUs and 31.3 GiB assigned. A measured idle snapshot used approximately 3.5 MiB for Server, 45 MiB for PostgreSQL, and 60 MiB for the canned provider; this is not a peak-memory or minimum-hardware claim. The runner image is approximately 810 MiB, excluding the separate Server/database images and generated state. The cached controlled workflow takes roughly two minutes on this host; initial dependency provisioning adds build time, and online runs depend on provider latency and pacing. Native Linux/macOS hosts and ARM64 remain unqualified.


Shared [capacity checks, workload measurement, image download sizes and native-host checklist](../../demo-qualification.md) apply to this demo. Reports describe the selected profile and retain failed outcomes.

## Held-out and stress qualification

The [fixture profiles](../../../src/order-exception-triage/fixture-profiles.json) define `default` (seed 41017, eight events), `heldout` (seed 841017, eight events), and `stress` (seed 941017, 80 events). Each event has its own procedure, indexed collection, scoped runbook and private expected result. Seeded order quantities are preserved in the exported packet and asserted alongside routes, missing-evidence flags and grounded explanation terms. The stress corpus repeats the eight authored scenario types with unique business identifiers and quantities; every generated case is qualified. It does not measure model quality across 80 distinct scenario types.

Manifests include generator/template revisions, profile, seed, event/document counts, logical clock, timezone, locale and content hashes. Separate JVM processes must produce identical corpus and oracle bytes for every profile. Generation refuses a different profile in existing input state. Online qualification requires default fixtures and retains the 3/3/2 provider assignment; its Compose profile enforces 60-second OpenRouter pacing. Only the canned fixture's request budget scales with corpus size.

Run `./tools/measure_demo.ps1 -Demo order-exception-triage -Project orders-heldout -Profile heldout` or `sh tools/measure_demo.sh order-exception-triage orders-stress stress`, choosing a new project name for each run.

The final 2026-09-11 profile results are:

| Profile and entry point | Measurement ID | Application tests passed | Elapsed | Sampled peak CPU | Sampled peak memory |
|---|---|---:|---:|---:|---:|
| Held-out, PowerShell | `37335ddce11a46129f6f69e3c4f3c935` | 31 | 214.26 s | 378.95% | 1,048,647,300 bytes |
| Stress, POSIX | `20260911T080753Z-cf043ffcb367b69f` | 103 | 212 s | 439.98% | 1,185,688,845 bytes |
| Default, fresh checkout through Ubuntu WSL | `20260911T080739Z-5b1c176956e03c38` | 31 | 250 s | 434.16% | 1,101,697,907 bytes |

Each run additionally passed 63 Java SDK tests with the one documented chronology skip. Stress includes all 80 business cases. The fresh checkout used snapshot `83dd77ccbb35246cc0faf09e54ec21e9f69807b1`, matching final source, and empty project state; its test run is `20260911T080740Z-7ab00d8bcf0b8eee`. Held-out and stress test IDs are `d8eaa2f8304e4f9ab1b8d3e664e770c5` and `20260911T080754Z-5edc6f25046d25f2`.

The held-out project's retained volumes occupy 53,583,965 logical bytes; stress occupies 83,186,920 bytes. The local runner is about 849 MB unpacked. Measurements ran in isolated projects on a shared Docker Desktop host, with other qualification work active; timings include build/cache effects, and 100% CPU denotes one core. Sampled peaks exclude host/VM/build-daemon overhead and may miss brief peaks. See the shared guide for compressed image download sizes. These WSL results qualify Linux userspace with Docker Desktop, not native Linux Engine, macOS, ARM64 or Apple Silicon emulation.

Initial profile runs exposed nondeterministic iteration order in the new manifest's nested count map. Canonical sorting fixed it; separate-JVM comparisons now pass. Failed measurements `371a20081cad484fa5c0508599376f69`, `20260911T080425Z-cb4cf445fb7a380c`, and `20260911T080409Z-13a249fae8d601da`, plus cloud invocation `6a8763a34b3646729d4713906b35ca95`, remain preserved. Those invocations stopped at fixture unit checks before business/provider qualification.

Final cloud invocation `c8aa871ad57441808213ad362de13964` passed all eight fresh cases, three on OpenAI, three on Anthropic and two on OpenRouter, with no skips or unresolved outputs. Case reports preserve actual provider/model identity and completion usage. Its unit phase also passed all 11 checks.
