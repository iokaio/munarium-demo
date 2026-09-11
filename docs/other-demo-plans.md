# Other Munarium demo plans

These proposals show everyday enterprise AI workflows as command-line tools, native desktop applications, scheduled jobs, queue consumers, and background services. None requires a browser application. They use new synthetic inputs, new shapes, and new runbooks; the existing demo scenarios and bundled data are outside the plan.

**Business case first.** Begin every `docs/demos/<demo-name>/README.md` with a **Business case** section immediately after its title, before screenshots, application details or setup instructions. Explain the operational problem, who encounters it, why a company might build a similar solution, and the expected business value. Identify the decisions that remain with people or existing business systems. Distinguish plausible benefits from measured outcomes; synthetic test results must not become claims of savings, productivity or operational safety. Apply this requirement to existing demo walkthroughs as well as future applications.

**Scope and provider requirements.** Leave the original web demo application alone, including its source, configuration, provider setup, runbooks, bundled data, and deployment workflow. Apply this plan only to the new demos and their isolated test harnesses. All real AI tests for these demos must use only the three online providers: **OpenAI, Anthropic, and OpenRouter**. Do not install or start local Ollama, download local completion models, add an Ollama test profile, or fall back to local inference when an online provider is unavailable. Keep keyless controlled fixtures for deterministic protocol and recovery tests; their canned responses do not perform model inference or count as online provider qualification.

The recommended first wave is an invoice exception batch, an employee policy desktop assistant, an order exception consumer, and a maintenance terminal. Together they give each Server client language a practical entry point. All thirteen planned applications are implemented, including the Matrix-backed inventory briefing. Each walkthrough records its own qualification evidence and remaining host-platform limits. Later demos teach historical reconstruction and evaluation. Recorded test results describe synthetic workloads, not measured business outcomes. Complete the remaining applications one at a time, commit each after its required qualification, and do not push; the developer will review and push the changes.

Every proposal must generate its own synthetic testing documents and data and run its automated test and integration harness locally through Docker. Support Docker Desktop using Linux containers on Windows and macOS, and Docker Engine with Compose or Docker Desktop on Linux. A fresh checkout must be sufficient to reproduce the fixtures and execute the keyless controlled suite after downloading the pinned dependencies and images; that suite must require no private dataset, enterprise account, paid model service, or hosted CI runner. Real AI qualification additionally requires Internet access and configured keys for all three online providers, with at least two independently asserted business scenarios per provider.

## Review basis and client coverage

Updated on 2026-09-10 against the local `munarium-demo` checkout and `munarium` commit `bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3`, which includes the merged Server 1.1.1 client alignment from PR #23.

The demo's [architecture](architecture.md), [configuration](configuration.md), [runbook guide](guides/runbooks.md), [customization guide](guides/customize-demo.md), and [development guide](guides/development.md) establish useful integration lessons. Code reviewed includes [MunariumClient.cs](../src/Demo.Web/Services/MunariumClient.cs), [TokenCache.cs](../src/Demo.Web/Services/TokenCache.cs), [ConversationCondenser.cs](../src/Demo.Web/Services/ConversationCondenser.cs), [Program.cs](../src/Demo.Web/Program.cs), and the generic corpus loader control flow. The important lessons to carry forward are:

- Use runbook sessions for access-filtered, multi-collection retrieval. A `complete: false` turn is a useful application capability in its own right.
- Preserve source identity, content hashes, and provenance alongside answers. Show missing evidence and remaining verification violations to the caller.
- Treat ingestion, index construction, and index activation as separate steps. Persist run IDs and approve only the cutovers belonging to the intended run.
- Keep identity and credentials explicit. The demo's capability cache includes the uid and clearance; a desktop application needs that same separation.
- A stored session transcript does not automatically make prior answers context for the next question. Applications must deliberately construct follow-up context and must not promote generated text into authoritative evidence.

The existing web app uses a custom HTTP wrapper. New examples should use the official source clients to teach their typed models, errors, and lifecycle. The language inventory below comes from `munarium/clients/README.md`, the four language READMEs, package manifests, and `clients/compatibility.json`.

| Application language | Server client | Natural demo form | Source dependency approach |
|---|---|---|---|
| Python 3.11+ | `munarium-client`, import `munarium_client` | Batch programs, scheduled analysis, evaluation tools; sync and async | Install `clients/python` from a complete checkout |
| C# / .NET 10 | `Ioka.Munarium.Client` | Native desktop tools and .NET workers; async | Project-reference `clients/dotnet/src/Ioka.Munarium.Client` |
| Java 21+ | `io.ioka.munarium:munarium-client` | Queue consumers, enterprise batch processing; sync and virtual-thread async | Gradle composite build including `clients/java` |
| Rust | `munarium-client` | Terminal applications, small agents, CI executables; Tokio async | Path dependency on `clients/rust/munarium-client`; wire-crate requirements are 1.1.1; use the checkout's toolchain |

All four have REST and gRPC transports, with documented gaps. Start with REST for a complete tutorial; add gRPC only where the demonstrated methods support it. Streaming turns, evidence reads, reports, bulk-upload sessions, authoring, and index builds require REST. A gRPC variant should expose unsupported operations clearly rather than silently changing transport.

The four Server clients now formally target **Server 1.1.1**, with supported minor versions **1.1 and 1.0** in `clients/compatibility.json`. Client package versions remain **1.0.0**, and MMP major remains **1**; package and Server versions are independent. Use the reviewed commit as the initial source baseline and pin Server 1.1.1 for these demos. The client documentation's dated publication check still records no public package releases, so build from a complete source checkout, including its `server/` dependencies. The merged alignment does not imply registry publication.

All four clients passed their existing REST/gRPC conformance checks against Server 1.1.1, with documented chronology skips. The new Python qualification suite additionally verifies Ollama and model routing through sync/async REST/gRPC clients and inspects REST streaming metadata. Its controlled provider proves protocol behavior, not real model quality or any proposed demo's business accuracy. The Server 1.0 baseline was not requalified in that run; it also lacks Ollama and the 1.1.1 routing fix. Implementation details and recorded results are in `munarium/clients/docs/guides/server-1.1.1.md`.

Matrix clients additionally exist for Python, C#/.NET, and Java. They talk to the separate Matrix service and are not extra Server languages. Their package versions and Matrix 1.0 compatibility are unchanged by the Server alignment. Only proposal 13 requires Matrix; proposals 1–12 can run with Server alone.

## Portfolio

Size is relative implementation scope, not a delivery estimate: **small** means one process and one main Server pattern; **medium** adds a durable workflow or native interface; **large** combines several patterns or an additional service.

| # | Demo | Language and application type | Main Server lesson | Size | Wave |
|---|---|---|---|---|---|
| 1 | Invoice exception packet | Python batch CLI | Grounded explanations alongside deterministic checks | Small | 1 |
| 2 | Employee policy assistant | C# native desktop | Scoped sessions, citations, streaming progress | Medium | 1 |
| 3 | Order exception triage | Java queue consumer | Independent sessions and durable work tracking | Medium | 1 |
| 4 | Maintenance procedure terminal | Rust TUI | Retrieval first, optional completion, source inspection | Small | 1 |
| 5 | Records intake service | C# background worker | Ingest, rebuild, verify, approve, resume | Medium | 2 |
| 6 | Master-data reconciliation | Java CLI | Disputed claims, corrections, optimistic concurrency | Medium | 2 |
| 7 | Shift handover journal | Rust daemon and CLI | Persistent facts, promises, historical pins | Medium | 2 |
| 8 | Policy change impact digest | Python scheduled job | Versioned evidence and bounded change analysis | Medium | 2 |
| 9 | Meeting commitment recorder | C# console workflow | Human review before governed memory writes | Medium | 3 |
| 10 | Quality investigation packet | Java batch job | Facts plus documents in an evidence hierarchy | Large | 3 |
| 11 | Engineering change review | Rust CI executable | Access-bounded AI in an existing delivery workflow | Medium | 3 |
| 12 | Retrieval evaluation bench | Python CLI and optional notebook | Measure retrieval, verification, latency, and usage | Medium | 2 |
| 13 | Inventory replenishment briefing | Python CLI; Java follow-on | Complete structured evidence through Matrix | Large | Extension |

## Wave 1: useful applications in every client language

### 1. Invoice exception packet — Python batch CLI

**Implementation status.** The [invoice exception demo](demos/invoice-exception/README.md) has source and tests in `src/invoice-exception/`, seeded synthetic inputs, the official Python client, and an isolated Docker workflow. After reorganization, its 29 application tests, 175 Python client tests, and 20 controlled invoice acceptance cases passed on Docker Desktop for Windows; four upstream chronology skips remain explicit. The distributed cloud suite also passed again with two tests on OpenAI `gpt-5.4-mini`, two on Anthropic `claude-haiku-4-5-20251001`, and four on OpenRouter `qwen/qwen3.8-flash`, without uncertain or unverified outcomes. Native Linux/macOS and ARM64 qualification remain pending. Its README and rendered PNG now share `docs/demos/invoice-exception/`.

**Use case and merit.** Accounts payable staff routinely compare invoices, purchase orders, goods receipts, and internal purchasing rules. A batch tool can explain discrepancies with citations and produce a review packet without requiring staff to ask the same questions repeatedly. It teaches where ordinary code should make exact calculations and where grounded AI adds useful prose.

**Build.** Accept a folder of new text-based invoices plus structured order and receipt exports. Generate roughly 20 synthetic cases: clean matches, partial receipts, duplicate invoice numbers, and missing documentation. Calculate amounts, quantities, and tolerances in Python. Ingest the fictional purchasing rules and supporting documents into scoped collections. Create a separate session per case and ask Server to explain the calculated exception using retrieved evidence. Validate any generated fields before producing CSV, Markdown, and a JSON evidence sidecar. OCR is an optional later exercise.

**Developer walkthrough.** Process one clean case, inspect the cited rule for an exception, then remove a required receipt and observe an explicit insufficient-evidence result. Show the deterministic calculation beside the generated explanation. Export a proposed action for review; payment execution belongs to the surrounding accounting workflow.

**Acceptance.** Totals match the independent fixture oracle; every rule cited resolves to a retrieved source; missing receipts never become fabricated confirmations. Re-running completed cases does not create duplicate packets.

### 2. Employee policy assistant — C# native desktop

**Implementation status.** The [employee policy assistant](demos/employee-policy-assistant/README.md) is implemented in `src/employee-policy-assistant/` with C#/.NET 10, Avalonia, and the official .NET client. Its documentation folder contains a README and PNG rendered from the actual window. The Docker harness generates seven fictional policy documents and eight independent business scenarios, tests desktop input and exports, and qualifies scoped sessions, bounded follow-ups, provider routing, expiry, and transcript recovery. On 2026-09-10, all 24 application tests and 82 .NET SDK tests passed, with two documented chronology skips. Six fresh cloud cases passed: equipment/training on OpenAI, regional access on Anthropic, and HR access on OpenRouter. Real AI testing uses only these three online providers. Consult the walkthrough for recorded results and remaining native OS limits.

**Use case and merit.** An employee or HR specialist wants a quick answer about equipment requests, training allowances, or leave procedures while working in desktop tools. This demonstrates an actual distributable client and the credential boundary that a backend-only sample does not teach.

**Build.** Use Avalonia with an async view model and the .NET SDK for a native desktop application on Windows, Linux, and macOS. Keep business logic separate from the view and use Avalonia's headless test platform for automated interaction tests inside the .NET Linux test container. Provide a question box, a source viewer, and export to a local text file. Seed new fictional employee-wide, regional, and HR-only documents. Supply short-lived query capabilities from a separate bootstrap issuer for the tutorial; document how an organization's authenticated token broker replaces it. Never ship a management token in the executable. The uid must match the capability subject. Use REST streaming to show phase progress, not simulated token streaming.

**Developer walkthrough.** Ask the same question with separately provisioned employee and HR accounts, open the source excerpt, then ask an explicit follow-up with bounded context. Change identity by ending the old session and opening a new one. Select an allowed model and show its identity on both expansion and completion progress. Demonstrate token expiry and a denied model override that is rejected before expansion calls a provider.

**Acceptance.** Restricted documents appear in neither hits nor generated answers for the employee account. Expiry recovery preserves identity. With a completing turn, the selected override reaches expansion and completion; a denied override makes no provider call. A disconnected stream produces an uncertain status followed by transcript inspection, without automatically paying for the same turn again.

### 3. Order exception triage — Java queue consumer

**Implementation status.** The [order exception triage demo](demos/order-exception-triage/README.md) is implemented in `src/order-exception-triage/` with Java 21, the official Java client through a Gradle composite build, an H2 inbox/outbox, and two bounded worker threads. It generates eight fictional events and scope-specific procedures with a separate business oracle. Its 29 application checks cover unit behavior, all eight business cases, access/expiry, duplicate delivery, provider outages, lost streams, a worker crash, and Server restart recovery. The Java SDK suite has 63 passing tests and one documented kernel-only chronology skip. The rebalanced cloud suite passed eight fresh cases: three on OpenAI, three on Anthropic, and two on OpenRouter. Earlier OpenRouter rate-limit failures remain recorded separately. Real AI qualification uses only these three online providers; the walkthrough records exact run outcomes and remaining host limits. The Rust maintenance procedure terminal is implemented in proposal 4. Continue one application per commit on `newdemos`, without pushing until requested.

**Use case and merit.** Fulfillment teams receive order-hold events throughout the day. A worker can attach a cited explanation and an internal routing suggestion before a person opens the exception. This is a practical pattern for adding AI to an existing enterprise event flow.

**Build.** Start with a durable local inbox containing synthetic JSON events; add a broker adapter after the core workflow works. Use Java 21, the official client, and a bounded executor. Retrieve fictional shipping, substitution, and escalation procedures through one session per order event. Keep order state in the input record; use the model for explanation and proposed routing. Validate routing against an application-owned enum. Persist event ID, input hash, session ID, processing state, and the reviewed-output outbox in a small local database. Order cancellation and ERP updates remain adapter extensions.

**Developer walkthrough.** Consume a held order, inspect its packet, deliver the event twice, and restart the worker after a turn was submitted. Reconcile the saved session before deciding whether another attempt is needed. Explain that an empty transcript immediately after a disconnect does not prove that the original turn has stopped.

**Acceptance.** Duplicate delivery creates one local output record. Each order has isolated context. Provider failure leaves a resumable or review-required item, and uncertain outcomes never trigger an immediate duplicate turn.

### 4. Maintenance procedure terminal — Rust TUI

**Implementation status.** The [maintenance procedure terminal](demos/maintenance-terminal/README.md) is implemented in `src/maintenance-terminal/` with Rust/Tokio and the official Rust client. Its trusted catalogue selects exact asset/revision scopes; retrieval works without a model, while an explicit explain action produces a cited answer. Twenty-four application checks and 79 official SDK unit/doc/conformance tests passed on Docker Desktop for Windows. The corpus contains eight cases and 24 synthetic documents, with current/historical selection, missing-evidence abstention, source hashes, expiry/access checks and transcript recovery. The final online suite passed eight fresh cases with the preferred balance of three OpenAI, three Anthropic and two paced OpenRouter cases. A fresh-checkout POSIX run also passed with matching fixture hashes. The walkthrough records exact results, retained failed runs and remaining native-host limits. Wave 1 now has all four language entry points.

**Use case and merit.** A facilities technician needs the procedure for a particular fictional asset revision from a workstation or terminal session. Fast evidence inspection is valuable even when model completion is disabled. This keeps the Rust entry point small and shows Munarium as a retrieval service.

**Build.** Create a Tokio application with a simple terminal interface and a plain stdout mode for scripts. Generate maintenance manuals, inspection notes, and revision notices for a few fictional assets. Select the applicable runbook/collection scope from trusted configuration. First issue a retrieval turn with `complete: false`; offer a separate explain action that requests completion. Display source path, chunk identity, content hash, and the actual collections searched. REST supports progress events; a later gRPC variant can use unary turns.

**Developer walkthrough.** Find a procedure, inspect an older revision as historical evidence, and ask about an unsupported asset. Show how the application identifies the applicable revision instead of asking the model to guess. A retrieval-only preset should disable model query expansion and omit model overrides when demonstrating operation without a completion provider. The explain action uses a named OpenAI, Anthropic, or OpenRouter configuration with preferred models and explicit tiers, showing how the same Rust client connects to Server for online completion.

**Acceptance.** Unsupported assets produce no actionable invented procedure. The chosen revision is visible in the evidence. Retrieval works with the completion provider unavailable under that preset. No machine-control command is part of this demo.

## Wave 2: enterprise integration and reliable memory

### 5. Records intake service — C# background worker

**Implementation status.** The [records intake demo](demos/records-intake/README.md) is implemented in `src/records-intake/` with .NET 10 and the official client. Its eight synthetic records exercise stable-file reconciliation, separate upload and binding, operator builds, verified cutovers and durable recovery. All 19 application tests and 82 SDK tests passed, with two documented kernel-only chronology skips. A fresh checkout repeated the complete suite through the POSIX entry point on Docker Desktop for Windows and regenerated identical fixture manifests. It configures no models, so cloud qualification is explicitly not applicable. The walkthrough contains exact report IDs and host limits.

**Use case and merit.** Departmental documents arrive in shared folders and must become searchable reliably. A .NET Worker Service demonstrates the often missing operational half of an AI application: publishing usable evidence.

**Build.** Use periodic directory reconciliation, with file notifications only as a wake-up hint. Generate a fresh set of office procedure documents and department routing rules. Wait for stable files, compute hashes, and persist logical filename, content hash, upload state, and index run ID. Use ingest capabilities for uploads and a separate operator command for runbook execution and approval. Exercise explicit collection binding and inspect per-file outcomes. Keep local states distinct: discovered, uploaded, bound, indexed, verified, and active. Use the runbook executor for normal index lifecycle; operator datastore routes are not all wrapped by the SDKs.

**Developer walkthrough.** Add a document, observe that upload alone does not make it searchable, inspect and approve the recorded cutover, then replace the file and restart midway through the next build. Resume by recorded IDs.

**Acceptance.** An unchanged file is recognized as a replay; changed bytes trigger a rebuild. Unauthorized collection binding fails. A failed build leaves the previously active index serving, and the approval command cannot approve an unrelated run.

### 6. Master-data reconciliation — Java CLI

**Implementation status.** The [master-data reconciliation demo](demos/master-data-reconciliation/README.md) is implemented in `src/master-data-reconciliation/`. It normalizes two synthetic exports, creates cited stewardship drafts, records explicit reviews, and imports typed baseline, disputed and corrected claims through a trusted writer. All 26 application checks, including competing worker containers, and 63 Java SDK checks passed, with one documented kernel-only chronology skip. A fresh checkout repeated the full keyless suite through the POSIX wrapper with matching fixture manifests. The final eight-case cloud run passed with a 3/3/2 OpenAI/Anthropic/OpenRouter split; an earlier upstream OpenRouter rate-limit/timeout failure remains documented and uncertain. The README records exact runs, versions and pending native-host coverage.

**Use case and merit.** Procurement and warehouse exports disagree about supplier lead times, packaging units, and product descriptions. A reconciliation tool shows why retaining both assertions and their resolution is more useful than letting an AI overwrite a master record.

**Build.** Generate two conflicting CSV exports and a fictional stewardship policy. Parse and normalize keys deterministically. Use a retrieval session to explain possible resolutions; any suggested mapping remains a draft. Through a trusted ledger-writing process, create a version and propose typed claims under a shape. Display `is_disputed` and gate findings as recorded outcomes. Keep a local proposal journal linking input rows, hashes, reviewer decisions, and returned claim IDs; reserve connector `origin` for actual connectors. Use the client write loop for head conflicts, rebuilding against fresh state.

**Developer walkthrough.** Submit incompatible claims, inspect the dispute, have a steward select a supported correction with `supersedes_id`, and compare head facts with a positive `as_of_seq` from before the correction. A second writer should deliberately cause a head conflict.

**Acceptance.** Disputed claims are retained and excluded from accepted facts. The historical read retains the old value. Rebuilt attempts use fresh keys; replays of completed identical commands use the saved key and body. A ledger acceptance is never presented as proof that a source was factually correct.

### 7. Shift handover journal — Rust daemon and CLI

**Implementation status.** The [shift handover journal](demos/shift-handover/README.md) is implemented in `src/shift-handover/`. Its daemon journals reviewed claims, anchors and promises before dispatch, and its CLI composes current or consistently pinned historical briefs. Twenty-one application checks, including competing worker containers, and 79 Rust SDK checks passed in both the controlled run and a fresh-checkout POSIX run with identical fixture manifests. The tutorial deliberately uses no model inference, so online qualification is not applicable. The walkthrough explains Server's current-head metadata on historical responses and its protected-section overflow for tiny composition budgets.

**Use case and merit.** Operations teams need persistent knowledge of equipment status, unresolved work, and commitments between shifts. This demonstrates Munarium's memory kernel beyond an ephemeral chat history.

**Build.** Supply a daemon that accepts local newline-delimited shift events and a CLI that reads or reviews them. Generate a fictional facility's status updates, inspection milestones, and open commitments. A trusted writer maps reviewed events into claims, anchors, and promises and checkpoints each completed write. Save the ledger version and sequence at shift close. Use `compose_context` with a token budget for the brief; optional narrative generation can use a Server research profile with the explicit fact version and supporting procedure documents. Keep generated commentary out of accepted facts unless separately reviewed.

**Developer walkthrough.** Record an unresolved commitment, close a shift, fulfill it in the next shift, then read both head and the earlier pinned view. Reduce the composition budget and inspect what was retained or omitted.

**Acceptance.** The earlier view still shows the commitment open. A daemon restart recovers completed work from the journal. Historical fact reads use the same pin consistently; a fresh model-generated brief is not claimed to be byte-for-byte replay of an earlier answer.

### 8. Policy change impact digest — Python scheduled job

**Implementation status.** The [policy change impact digest](demos/policy-change-digest/README.md) is implemented in `src/policy-change-digest/`. It separates before, after and downstream checklist evidence, computes textual diffs, exports cited candidate impacts and checkpoints each revision pair. The controlled run passed 23 application checks and 175 Python SDK checks, with four documented chronology skips. The fresh-checkout POSIX suite passed with matching fixture hashes, and all eight online cases passed with three OpenAI, three Anthropic and two paced OpenRouter cases.

**Use case and merit.** Internal process owners need to know which checklists and training notes may be affected when company procedures change. A scheduled digest brings relevant analysis to an existing workflow without adding a UI.

**Build.** Generate before/after versions of fictional purchasing, travel, and equipment policies plus downstream checklists. Save each source revision under an explicit revision identity and build versioned collections/runbooks. Compute textual changes in Python. Query for potentially affected documents and request a cited explanation of each candidate impact. Write Markdown and CSV digests to disk with input hashes and version references; email delivery can be an independently configured adapter. Checkpoint by revision pair and do not ingest generated digests back into the authoritative policy collection.

**Developer walkthrough.** Change an approval threshold, inspect the exact diff, then inspect why a checklist was suggested for review. Re-run the same revision pair and show that the completed digest is reused.

**Acceptance.** The seeded impacted checklist appears among the candidates; an unrelated checklist is not asserted to require a change. Reports clearly say candidate impacts: top-k document retrieval cannot establish that every affected document has been found. Old and new evidence stay distinguishable.

### 12. Retrieval evaluation bench — Python CLI and optional notebook

**Implementation status.** The [retrieval evaluation bench](demos/retrieval-evaluation/README.md) is implemented in `src/retrieval-evaluation/`. It collects real Server responses under separate public and privileged identities, scores independent source labels offline, compares one retrieval setting at a time, and exports repeated completion metrics and static plots. The controlled run passed 23 application checks covering 72 experiment records and 175 Python SDK checks, with four documented chronology skips. The fresh-checkout POSIX suite passed with matching fixture hashes; repeated online qualification passed all eight cases and 16 fresh completions, balanced six OpenAI, six Anthropic and four paced OpenRouter completions.

**Use case and merit.** A team choosing retrieval settings needs evidence that its application finds the right material before tuning answer wording. This demo supports every other proposal and turns trial-and-error into a repeatable development process. The CLI is complete on its own; notebooks are optional.

**Build.** Generate a small, new enterprise procedure corpus with explicit question-to-source labels, ambiguous questions, unavailable answers, and two access profiles. Keep the answer key outside ingested sources. Run retrieval-only baselines and then completion runs against named shape/runbook versions. Compare top-k, candidate counts, context budgets, and allowed model overrides one variable at a time. Persist raw responses and export CSV, Markdown, and static plots. Use a separate management client to collect usage reports; provider token counts are not automatically monetary cost.

**Developer walkthrough.** Start with a missed source, inspect retrieved chunks, change the relevant setting, and compare baseline and candidate. On Server 1.1.1, a session model override changes both expansion and completion; report it as a combined change. To isolate completion quality, hold query expansion fixed in versioned runbook task settings and omit the turn override. Include a fluent answer whose citation is irrelevant to demonstrate why language quality alone is an insufficient pass condition.

**Acceptance.** Score source recall at k against independent labels, citation resolution, abstention cases, access leakage, and remaining verification violations. Report latency and token usage for a fixed workload. For variable model output, retain repeated-run results and uncertainty; avoid snapshot tests that require identical prose. Fail the access-isolation regression on any forbidden-source exposure.

## Wave 3: combine patterns into deeper workflows

### 9. Meeting commitment recorder — C# console workflow

**Use case and merit.** Project teams lose decisions and commitments in meeting notes. Turning reviewed notes into persistent memory is a familiar AI task that makes the boundary between generated suggestions and accepted records visible to developers.

**Build.** Start with text transcripts from fictional project meetings, not audio capture. Ingest them into a project-scoped collection. Retrieve relevant passages and request candidate decisions, owners, and due dates. Parse and validate candidates in application code, then write a local review file. A separate review/import command records approved claims and promises through a trusted ledger writer and saves transcript hashes and claim IDs. Ambiguous owners and relative dates require review rather than invented resolution.

**Developer walkthrough.** Extract a commitment, reject a suggestion that was never agreed, approve a supported item, then record a corrected due date using supersession. Query unresolved commitments and inspect the original passage.

**Acceptance.** No candidate reaches the ledger before the review step. Rejected items remain only in the local review history. Corrected dates can be read both before and after the correction with pins. Reminders are local preview output until an organization adds its own delivery adapter.

**Implementation status.** Meeting commitments is implemented in [its source folder](../src/meeting-commitments) and [walkthrough](demos/meeting-commitments/README.md). Controlled qualification passed 24 application tests and 82 official .NET tests, with two documented chronology skips. The fresh-checkout POSIX run passed the same checks; all eight online cases passed with the 3/3/2 allocation and paced OpenRouter calls.

### 10. Quality investigation packet — Java batch job

**Use case and merit.** Manufacturing teams assemble defect observations, inspection results, and applicable procedures before a quality review. Combining structured facts with explanatory documents teaches a richer form of evidence use than a single document search.

**Build.** Generate fictional lot observations and inspection records plus quality procedures. Have a trusted process propose validated observations into a dedicated ledger version. Record a positive sequence for historical fact queries and make an explicit `facts:<version_id>` binding in the investigation runbook's research profile, with procedure collections as another layer. The fact-version binding is per runbook, not per conversation. Stop further writes to the investigation version by application policy, and create a child version and a new runbook version for subsequent corrections. Do not imply that a session turn accepts the same historical pin as a direct ledger query. Use Java batch processing to export observations, discrepancies, citations, and open questions. Compare structured fields deterministically where possible; the hierarchy does not automatically detect every fact/document disagreement.

**Developer walkthrough.** Introduce a conflict between an inspection fact and a narrative note. Inspect each layer's role and show unresolved disagreement in the packet. Add a later correction and compare it with the earlier record.

**Acceptance.** The packet labels observation, supported conclusion, and hypothesis separately. Missing or refused required evidence prevents a complete-packet status. A procedure search never establishes a complete defect count. Root-cause determination and disposition remain reviewer decisions.

**Implementation status.** Quality investigation is implemented in [its source folder](../src/quality-investigation) and [walkthrough](demos/quality-investigation/README.md). It combines frozen fact bindings and required procedures, preserves disagreements, and creates reviewed child-version corrections. Controlled and final fresh-checkout runs passed 22 application tests and 63 official Java tests, with one documented chronology skip. All eight final online cases passed with the 3/3/2 allocation and paced OpenRouter calls; earlier failed runs remain documented.

### 11. Engineering change review — Rust CI executable

**Use case and merit.** Engineering teams want relevant architecture decisions, internal standards, and release procedures beside a proposed change. A CI tool delivers cited review suggestions in the workflow developers already use.

**Build.** Read a supplied diff and change manifest. Generate fictional design decisions and release procedures as the corpus. Map component IDs to allowed runbooks using trusted configuration; treat diff content as untrusted input. Retrieve applicable requirements and request a bounded review. Export Markdown and JSON artifacts, with source hashes, session ID, model identity, and any verification violations. Keep deterministic checks, such as a required manifest field, in Rust. Use query-scoped CI credentials; runbook administration belongs to a separate bootstrap job. Comment publication is an optional adapter.

**Developer walkthrough.** Review a change that omits a documented migration step, inspect the cited requirement, then add an irrelevant instruction inside the diff and show it cannot select a more privileged collection. Run the same diff under a different permitted component scope.

**Acceptance.** No secrets or denied sources reach the artifact. Findings cite retrieved requirements. Deterministic failures can fail CI; model suggestions are advisory by default, with a distinct exit status for unavailable or unverified analysis. A failed network call does not cause blind turn replay.

**Implementation status.** Engineering change review is implemented in [its source folder](../src/engineering-change-review) and [walkthrough](demos/engineering-change-review/README.md). Controlled and fresh-checkout qualification passed all 25 application checks and 79 official Rust client checks. All eight online cases passed with the 3/3/2 allocation and paced OpenRouter calls. Deterministic CI failures, advisory findings and unavailable analysis have separate outcomes.

## Optional extension: complete structured evidence

### 13. Inventory replenishment briefing — Python CLI, optional Java follow-on

**Implementation status.** The [inventory replenishment briefing](demos/inventory-replenishment/README.md) is implemented in [its source folder](../src/inventory-replenishment). Controlled and fresh-checkout qualification passed 28 application checks, 175 official Server Python checks with four documented chronology skips, and all 20 official Matrix Python checks including the live service test. All eight final online cases passed with the 3/3/2 allocation and paced OpenRouter calls. The app enforces complete governed rows, parameter identities, business freshness and resolvable citations; failed runs remain documented. The optional Java port remains an extension.

**Use case and merit.** A planner asks which items are below reorder level and why replenishment is constrained. The list and counts must come from a complete governed result; policy documents supply explanation. This demonstrates when Server retrieval should be combined with Matrix.

**Build.** Add Matrix and a small synthetic inventory database. Register a datasource and a parameterized contract for below-threshold inventory, with defined keys, freshness, and completeness expectations. Use the Python Matrix client to apply and verify that configuration, then the Server client to run a session whose research profile combines the declared Matrix data view with fictional replenishment procedures. Let Matrix produce sealed evidence; the application reads manifests and bounded row pages through Server's REST evidence plane. Export CSV and a cited Markdown briefing. A Java port can exercise the same fixtures through its two corresponding clients.

**Developer walkthrough.** Inspect the exact result and row citations, then make the structured source unavailable, stale, or truncated. Show the refusal or incomplete status and how it changes what the briefing may claim.

**Acceptance.** Exact counts match the database fixture only when the governed result supports completeness. Document hits never substitute for missing inventory rows. Evidence access respects the caller's clearance, and an expired artifact produces an explicit unresolved citation. The model selects declared contracts; it does not generate unrestricted SQL. Reorder execution is outside the briefing's responsibility.

## Common implementation and teaching contract

Each demo should be independently runnable and explain the application/Server boundary. Server provides retrieval, sessions, governed memory, and configured completion. The application supplies business calculations, scheduling, inboxes, review steps, output validation, and integrations with systems of record.

### Repository structure and first successful run

Use a separate `src/<demo-name>/` directory per application for native-language source, dependency locks, fixture generators, `runbooks/`, `shapes/`, Docker configuration, and tests. Put each developer walkthrough and its recorded validation in `docs/demos/<demo-name>/README.md`, indexed by `docs/demos/README.md`. Commit generator code, templates, schemas, and oracle definitions; generate documents and data into ignored directories. Provide equivalent PowerShell and POSIX shell entry points for the containerized workflow. A shared bootstrap helper may reduce repetition, but each demo's documentation must show the actual client calls in its language.

Use the following repository-relative locations. The application folders below contain the planned implementations. Keep native project structure beneath each source folder, including any desktop and worker projects belonging to that demo. Each documentation folder must contain its `README.md` walkthrough and an `application.png` rendering linked from that README.

| # | Source, fixtures, tests, and Docker files | Developer walkthrough |
|---|---|---|
| 1 | `src/invoice-exception/` | [docs/demos/invoice-exception/README.md](demos/invoice-exception/README.md) |
| 2 | `src/employee-policy-assistant/` | `docs/demos/employee-policy-assistant/README.md` |
| 3 | `src/order-exception-triage/` | `docs/demos/order-exception-triage/README.md` |
| 4 | `src/maintenance-terminal/` | `docs/demos/maintenance-terminal/README.md` |
| 5 | `src/records-intake/` | `docs/demos/records-intake/README.md` |
| 6 | `src/master-data-reconciliation/` | `docs/demos/master-data-reconciliation/README.md` |
| 7 | `src/shift-handover/` | `docs/demos/shift-handover/README.md` |
| 8 | `src/policy-change-digest/` | `docs/demos/policy-change-digest/README.md` |
| 9 | `src/meeting-commitments/` | `docs/demos/meeting-commitments/README.md` |
| 10 | `src/quality-investigation/` | `docs/demos/quality-investigation/README.md` |
| 11 | `src/engineering-change-review/` | `docs/demos/engineering-change-review/README.md` |
| 12 | `src/retrieval-evaluation/` | `docs/demos/retrieval-evaluation/README.md` |
| 13 | `src/inventory-replenishment/` | `docs/demos/inventory-replenishment/README.md` |

Each source folder owns its `Dockerfile`, `.dockerignore`, `compose.yaml`, optional `compose.cloud.yaml`, dependency locks, `tests/`, `local.ps1`, and `local.sh`. Write generated exports, journals, manifests, and test reports under ignored `artifacts/<demo-name>/`; keep private oracle and capability mounts separate from those reviewable outputs. Share the repository-root `.env.local` and [`.env.local.sample`](../.env.local.sample) across demos. Exclude secrets and generated artifacts from build contexts. Add each completed walkthrough to the [demo index](demos/README.md), and keep commands runnable from the repository root. Write prose paragraphs on single physical lines.

### Application rendering for every demo

Commit a PNG at `docs/demos/<demo-name>/application.png` and embed it near the beginning of that demo's `README.md` with descriptive alt text and a caption. Show the implemented application using newly generated fictional inputs. Render native windows or terminal interfaces directly where possible; for background workers and batch programs, render a representative terminal run and its actual exported output. Label an illustrated terminal/output composition as a rendering rather than an operating-system screenshot. Use readable text and enough context to show the workflow's purpose. Never include credentials or private inputs. Record a reproducible capture command and refresh the PNG whenever the demonstrated interface or output materially changes. Review the resulting image and run the documentation link checker before committing.

| Demo | Required subject for `application.png` in its documentation folder |
|---|---|
| 1. Invoice exception | Batch terminal and generated invoice review packet with deterministic amounts and citations |
| 2. Employee policy assistant | Desktop question, selected identity/model, phase progress, answer, and source excerpt |
| 3. Order exception triage | Queue-consumer terminal with a processed exception and duplicate-event disposition |
| 4. Maintenance terminal | TUI question, applicable manual revision, and inspected source |
| 5. Records intake | Worker status showing ingest, index verification, pending approval, and activation |
| 6. Master-data reconciliation | CLI dispute, reviewed correction, and resulting recorded head |
| 7. Shift handover | Journal CLI with open commitments and a historical view |
| 8. Policy change digest | Scheduler terminal and generated change-impact digest |
| 9. Meeting commitments | Console candidate review and approved commitment record |
| 10. Quality investigation | Batch status and generated evidence packet with missing/conflicting observations |
| 11. Engineering change review | Local CI command and cited review output with access boundaries |
| 12. Retrieval evaluation | Evaluation command and measured retrieval/grounding report |
| 13. Inventory replenishment | CLI briefing with governed counts, cited rows, and completeness status |

Use these implemented files as references, adapting them to each application's language and business cases:

| Concern | Invoice reference |
|---|---|
| Pinned client checkout and isolated containers | [Dockerfile](../src/invoice-exception/Dockerfile), [compose.yaml](../src/invoice-exception/compose.yaml) |
| Equivalent Windows and POSIX workflows | [local.ps1](../src/invoice-exception/local.ps1), [local.sh](../src/invoice-exception/local.sh) |
| Deterministic corpus and independent oracle | [fixtures.py](../src/invoice-exception/invoice_demo/fixtures.py) |
| Bootstrap, scoped capabilities, and approved indexes | [server.py](../src/invoice-exception/invoice_demo/server.py) |
| Durable work and transcript recovery | [batch.py](../src/invoice-exception/invoice_demo/batch.py) |
| Controlled failure and recovery tests | [test_integration.py](../src/invoice-exception/tests/test_integration.py) |
| Provider assignment and real-model assertions | [cloud_plan.py](../src/invoice-exception/invoice_demo/cloud_plan.py), [test_cloud.py](../src/invoice-exception/tests/test_cloud.py), [quality.py](../src/invoice-exception/invoice_demo/quality.py) |

Keep first-run fixtures to tens of records, with an explicit larger stress profile. Follow the synthetic generation contract below for every document, structured record, event, and integration response. Expected answers must live outside ingested content. Do not copy existing demo or data assets.

The walkthrough should move through the same concrete stages:

1. Run the local preflight, generate and verify synthetic fixtures, then start or attach to compatible local Server 1.1.1 and PostgreSQL containers. Verify `GET /version` and record the client commit and image digests. Use the controlled provider for protocol tests and the three configured online providers for real answer-quality tests; configure named providers only where needed.
2. Run a separate bootstrap command to apply shapes, providers where required, and versioned runbooks. Provision application identities and credentials.
3. Load the new inputs, build indexes where applicable, inspect the recorded run, and explicitly approve its cutovers. Check an actual retrieval result; process readiness alone does not prove the corpus is usable.
4. Run one business case and inspect its output, evidence, and call sequence. For a ledger demo, inspect accepted/disputed status and the recorded head.
5. Inject a failure or conflicting input, recover with saved state, and run the independent acceptance checks. Document exactly what cleanup removes.

### Reproducible synthetic documents and data

Generate all inputs in a pinned generator container so host Python versions, locale, and path rules cannot change the dataset. Commit a default seed, generator version, fixed logical clock, timezone, locale, and size profile. Derive stable business identifiers and explicit document revisions from those inputs. Use UTF-8, canonical ordering, normalized line endings, relative paths, and fixed metadata for formats that embed dates or random identifiers. Do not use a live LLM to generate baseline test fixtures; templates and seeded rules must work without inference or network access.

Each generation run must emit a manifest containing the seed, generator and template revisions, profile, record counts, relative filenames, and SHA-256 hashes. Generate twice into separate empty directories and require identical manifests and bytes for the same configuration. Keep runtime Server IDs, credentials, and execution timestamps in the run report rather than the canonical input manifest. Include portable filename cases and Unicode content, but avoid filenames that are invalid on a supported host. Mark every document and export as fictional.

Generate an independent oracle alongside the corpus: expected arithmetic, entity relationships, access classifications, source relevance labels, version transitions, and completeness expectations. Assert exact outcomes with ordinary code wherever possible; assess model prose by grounded claims and explicit rubrics. Store oracle files in a separate volume that the ingestion process, Server, and model cannot read. Derive the oracle from declared scenario facts and rules, never from the application's output. Include positive, missing, malformed, duplicate, conflicting, superseded, stale, and unauthorized cases, with separate tuning and held-out regression seeds.

The generated scenarios must cover each proposal's business boundary:

| Demo | Generated documents and data | Independent expected outcome |
|---|---|---|
| 1 | Invoices, purchase orders, receipts, purchasing rules | Exact amounts, tolerances, duplicates, and missing support |
| 2 | Employee, regional, and HR policies; synthetic identities | Allowed sources per identity, denied access, expiry behavior |
| 3 | Order events, duplicate deliveries, routing procedures | One local output per event and valid routing values |
| 4 | Asset revisions, manuals, inspection notes | Applicable revision, relevant sources, unsupported assets |
| 5 | Staged folder arrivals, replacements, partial files | Content hashes and intended index activation transitions |
| 6 | Conflicting supplier and product exports | Disputes, reviewed corrections, historical values |
| 7 | Shift events, commitments, fulfillment records | Open and fulfilled states at recorded historical pins |
| 8 | Policy revisions and dependent checklists | Seeded candidate impacts and revision-pair deduplication |
| 9 | Meeting transcripts and scripted reviewer decisions | Approved commitments, rejected candidates, corrected dates |
| 10 | Lot observations, inspections, conflicting narrative notes | Exact observations, missing evidence, unresolved conflicts |
| 11 | Diffs, change manifests, design and release procedures | Required fields, relevant requirements, denied source boundaries |
| 12 | Labeled questions, procedure corpus, access profiles | Source recall, abstention, citation validity, leakage checks |
| 13 | Inventory rows, replenishment procedures, stale and truncated source variants | Exact governed rows/counts and incomplete-result handling |

### Local Docker environment on Windows, Linux, and macOS

The shared [qualification guide](demo-qualification.md) documents the implemented capacity preflight, workload measurement commands, pinned-image architecture inventory, and supplementary native desktop checklist. Individual walkthroughs record which fixture and host profiles actually passed.

Use the same Linux Compose services and test commands on all three host operating systems. Keep paths repository-relative and pass arguments without shell-specific interpolation; thin `.ps1` and `.sh` wrappers must execute equivalent container commands and failure checks. The host prerequisites are Git and a running local Docker installation with Compose. Put each demo's native language runtime, generators, and test tooling in pinned runner images. Fetch the complete Munarium checkout at the pinned commit during the runner build, as the invoice Dockerfile does, so a second host checkout is unnecessary. Do not change Server or Matrix source. Keep platform-specific build outputs and dependency caches in separate named volumes rather than sharing host-built binaries with Linux containers.

Preflight must verify the Docker context is local, the Linux engine and Compose are available, required ports are free, and adequate memory and disk space are available for the selected profile. During implementation, measure and document CPU, memory, disk, download sizes, and approximate runtime for the smallest complete CPU-only workload. No local completion inference or GPU is required. Pin image digests and record the exact online provider/model identifiers. Check every dependency's architecture support and record the selected platform; qualify native `linux/amd64` and `linux/arm64` where available, and document and test an explicit emulation profile for images unavailable natively, including Apple Silicon. Do not claim cross-platform qualification until runs on Windows, Linux, and macOS are recorded.

Inventory existing containers before provisioning. Reuse a compatible local development service only when explicitly selected, healthy, version-checked, and able to provide isolated test state. Prefer reusing cached images and dependencies while creating fresh project-scoped containers and volumes for qualification. Create any missing service through Compose. Never reset an unrelated database or stop an unrelated container; restart, outage, and corruption tests must use dedicated disposable resources. Attach selected existing services through documented network/endpoint settings, and record their identities in the report.

The base stack contains Server 1.1.1, PostgreSQL with the required extensions, the controlled Ollama-protocol fixture, and separate language test runners. Real AI tests use only OpenAI, Anthropic, and OpenRouter; do not provision Ollama containers or download local completion models. Add Matrix with its configured dependencies under the inventory profile. The lightweight protocol fixture serves canned responses without loading a model. This restriction applies to the new demos; preserve Ollama support in the original web demo. Use service DNS names for container-to-container traffic, including provider endpoints resolved by Server; `localhost` inside a test runner is not Server. Publish only necessary host ports on loopback for native desktop use and diagnostics. Separate generated inputs, private test oracles, outputs, and database state into appropriate mounts or volumes.

Run integration dependencies locally as well. Proposal 3's broker adapter requires a local broker container and synthetic duplicate/redelivery events. Proposals 8 and 9 use a local mail sink if delivery adapters are implemented. ERP and source-control adapters use local contract fixtures that record requests and inject failures; these tests establish the declared adapter contract, not qualification of a live vendor deployment. Proposal 13 requires a real local Matrix container and a separately seeded inventory database. Proposal 5 must exercise a repository-relative host folder mounted into its worker container, testing periodic reconciliation even when file notifications differ across hosts. Proposals 6 and 7 use competing worker containers to exercise write conflicts. Proposal 11 runs its CI executable locally, and proposal 12 executes any tested notebooks headlessly in its Python container.

For proposal 2, run application logic, view-model, and headless control/input tests in the .NET container, including real-Server identity switching, streaming progress, and export checks. Use Avalonia rather than the earlier Windows-only WPF proposal so all required automated tests are portable. Headless tests do not establish native rendering or operating-system integration; include a supplementary manual launch, source navigation, and export checklist on each supported desktop OS. See Avalonia's [headless testing](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) and [Docker deployment](https://docs.avaloniaui.net/docs/deployment/docker) guidance. Manual native checks do not replace the automated container suite.

### Local AI-provider key files

The invoice implementation established repository-root `.env.local` and [`.env.local.sample`](../.env.local.sample), with an explicit `/.env.local` ignore rule and an exception allowing the sample to be committed. Reuse this shared configuration for subsequent demos, preserving existing local keys and model preferences. The sample documents the format and contains empty key placeholders; credentials belong only in the ignored local file. Before the first credentialed run, stop for the developer to fill missing required keys. When the keys are already supplied, continue with the authorized tests without asking for them again.

The sample must use UTF-8 dotenv text with one `KEY=value` assignment per line, comments on their own lines beginning with `#`, and no shell `export` prefix. Empty values mean the optional provider is unconfigured. Document single-quoting values that contain spaces, `#`, or `$` so the Compose environment-file parser treats them literally. Include the following sample content; keep real AI test provider coverage limited to these three online providers:

```dotenv
# Copy this file to .env.local and enter keys only in that ignored file.
# Format: one KEY=value per line; leave unused provider values empty.
# Use single quotes around values containing spaces, #, or $.
# Controlled fixtures need no keys and run no models; real AI tests use the three online providers.

# OpenAI: consumed by the optional named OpenAI provider configuration.
OPENAI_API_KEY=
OPENAI_MODEL=gpt-5.4-mini

# Anthropic: consumed by the optional named Anthropic provider configuration.
ANTHROPIC_API_KEY=
ANTHROPIC_MODEL=claude-haiku-4-5-20251001

# OpenRouter: consumed by the optional named OpenRouter provider configuration.
OPENROUTER_API_KEY=
OPENROUTER_MODEL=qwen/qwen3.8-flash
```

Document copying the sample from the repository root with `Copy-Item .env.local.sample .env.local` in PowerShell or `cp .env.local.sample .env.local` in a POSIX shell, only when the destination does not already exist. Explain how each optional provider configuration maps its variable to a Server-resolved `credentialRef`. The sample must contain no working credentials.

Configure preferred models through `OPENAI_MODEL`, `ANTHROPIC_MODEL`, and `OPENROUTER_MODEL`, using the defaults above. Each demo that uses cloud completion must assign at least two independently asserted business scenarios to each provider. Allocate additional cases to OpenAI and Anthropic, keeping OpenRouter at two cases to reduce rate-limit exposure; an eight-case suite should use a 3/3/2 split. Distinct assignments are sufficient; repeating the full corpus on every provider is optional. Keep the complete controlled corpus as the regression baseline. The invoice-specific `INVOICE_PROVIDER` and `INVOICE_MODEL` settings in the sample support manual single-provider bootstrap; the distributed suite uses the three provider model variables. Add documented, namespaced settings only when another demo needs an equivalent manual selection.

Have the optional cloud-provider Compose profile load `.env.local` explicitly and pass only each configured provider's required variables to Server, where provider `credentialRef` values resolve them. Do not mount the key file into demo applications, fixture generators, or test runners. The default controlled profile must work when `.env.local` is absent or contains no keys. Never print resolved secrets in commands, Compose configuration dumps, logs, or exported run bundles; include checks that `.env.local` is ignored, `.env.local.sample` is tracked, and every sample key placeholder is empty.

### Credentials, state, and recovery

- Query applications use short-lived, runbook-scoped capabilities with the authenticated uid. Ingestion workers use ingest scope. Management credentials belong to the bootstrap/operator process. Core ledger writers require a trusted write-authorized service; do not imply that a query capability grants arbitrary ledger writes. Enterprise identity assignment is application-owned.
- Persist input hashes, application work IDs, session/run IDs, and completed command bodies and keys before advancing local workflow state. A local inbox or outbox deduplicates application outputs; it does not make Server turns exactly-once across arbitrary network failures.
- Use the SDK's typed error categories. Head conflicts require a fresh read, a rebuilt claim, and a fresh idempotency key. Identical completed command replays retain their key and body. An ambiguous command failure needs reconciliation: Server records keys after completion, so immediately retrying an in-flight command can execute it twice. Session turns must not be blindly retried either; inspect the transcript and keep unresolved work pending.
- A session pins its runbook and access context; ledger `as_of_seq` is a separate pin. Record document/index versions and fact pins where applicable. A ledger pin does not freeze live external data or make model prose deterministic. Use explicit research-profile configuration when combining facts and documents.
- Preserve actual searched/skipped collections, source references and hashes, provenance envelopes, and completion verification results. Retrieval scores are not probabilities of correctness. Syntactically valid citations and accepted claims still need business evaluation.
- Cap concurrent work and record token consumption. `complete: false` suppresses answer generation, but configured model query expansion can still invoke a provider. Keep monetary estimates separate and identify their pricing inputs.

Carry forward the Server 1.1.1 behaviors exercised by the invoice tests. A capability's `runbook_refs` contains bare runbook metadata names, while session creation pins `name@version`; the session uid must match the capability. Test access denial against an existing runbook outside the capability's scope and assert the SDK's typed forbidden error. Keep sessions and collection access isolated by business case and identity, and include input, runbook, shape, provider, and model revisions in configuration namespaces.

Resolve model citation labels such as `[collection/chunk_id]` against actual returned hits, preserving source paths and hashes. Assert citation resolution and business grounding separately. Save turn intent and the session ID before dispatch, save the response before atomic file exports, and prove that rerunning completed work restores outputs without another completion. Reconcile a lost response only when the saved transcript contains exactly one matching completed turn; leave ambiguous or empty transcripts uncertain instead of resubmitting automatically. Server 1.1.1 transcript completion routing is nested under `completion.resolved`, including `was_override`. Preserve that metadata when recovering a response, mark recovered evidence, and omit unavailable fields such as the live skipped-collection list instead of inventing empty values.

### Model configuration on Server 1.1.1

For real AI tutorials, apply named OpenAI, Anthropic, and OpenRouter configurations with Server-resolved credentialRef values, preferred models, and explicit tier mappings. Munarium API authentication is still required. Inventory credential_ok means credentials resolve or are unnecessary, not that the endpoint is healthy. Use named provider health to check connectivity and model availability; /healthai only probes cloud defaults. The keyless fixture uses the Ollama wire protocol with canned responses; it does not run Ollama or perform local model inference.

Set `complete: true` when sending a nonempty session `model_override`, allow its provider reference in the runbook, and teach that Server 1.1.1 applies it to both model query expansion and completion. Without an override, each task retains its configured model. Retrieval-only turns reject nonempty overrides. Capture actual provider/model identities and usage for each paid stage; selecting a higher tier can increase expansion cost too. Existing SDK request fields already express this behavior, so applications should not issue an extra provider call to implement the override.

### Validation and build order

Provide the following separate actions with equivalent PowerShell and POSIX entry points. Use the invoice commands below as the naming contract for subsequent demos, substituting their source folder. Run documentation/static checks alongside the applicable profiles when changing code or paths. Execute language conformance suites serially when they share a Server, and isolate mutable state between demo acceptance runs.

| Action | PowerShell from repository root | POSIX from repository root | Coverage and prerequisites |
|---|---|---|---|
| `test` | `./src/invoice-exception/local.ps1 -Action test` | `sh src/invoice-exception/local.sh test` | Keyless build, lint/unit checks, generated fixtures, real-Server controlled integrations, chosen SDK conformance, failure recovery, and the complete business acceptance corpus |
| `cloud` | `./src/invoice-exception/local.ps1 -Action cloud` | `sh src/invoice-exception/local.sh cloud` | Explicit real-AI qualification using all three configured online providers, preferred models, and at least two business tests per provider |
| `stop` | `./src/invoice-exception/local.ps1 -Action stop` | `sh src/invoice-exception/local.sh stop` | Stop this project's containers while retaining volumes, journals, and artifacts |

The default `test` action must work without `.env.local` and must not start paid calls merely because keys exist. A controlled pass establishes application and protocol behavior; report real-model qualification separately. Complete local qualification requires both controlled tests and the three-provider cloud acceptance suite for demos that use AI, plus Matrix checks for proposal 13. Record any unexecuted profile or host platform as pending. Smaller development commands are useful but do not satisfy the complete corpus requirement.

Begin with the merged client qualification assets: `clients/python/conformance/compose.server-111.yaml`, `ollama_fixture.py`, and `test_server_111.py` in the Munarium repository, and the invoice container harness linked above. Supply the fixture endpoint variables explicitly using container-reachable addresses; an opt-in test skip is not a pass. For each demo, run its chosen client's documented build, lint, unit, and REST/gRPC conformance checks; Python demos also use the sync/async release-specific routing and REST streaming checks. Keep each language's native test tooling in its runner. Recheck compatibility with `clients/check_compatibility.py` when changing the pinned client revision, and preserve documented transport gaps.

Every demo needs a real-Server smoke run and all of its named acceptance cases against freshly generated business inputs. Verify ingestion, retrieval, and intended cutovers before judging business results. Exercise access denial, expired credentials, provider unavailability, interrupted streams, duplicate work, worker restarts, and applicable head conflicts. Use a local fault proxy or controlled fixture for network failures and dedicated containers for service restarts; preserve checkpoints and verify recovery after the dependency returns. Test time-sensitive cases with explicit logical time where supported and bounded waits for real expiry behavior.

The controlled provider returns canned responses, so separately run answer-quality and grounding cases against the preferred OpenAI, Anthropic, and OpenRouter models. Do not run these new demo tests against Ollama or provision local completion models. After initial dependency provisioning, the controlled suite and synthetic generator must run using local containers without external services. Real AI qualification requires Internet access and the configured provider keys; a keyless pass alone does not establish answer quality. Record exact provider/model identifiers, usage, quality results, and declared acceptance thresholds; identical synthetic inputs do not imply identical model prose or latency.

For the explicit cloud action, keep an explicit provider-to-case assignment in source and assert every assigned case against the private oracle. The table below preserves the historical invoice reference. The current invoice suite was rebalanced and passed on 2026-09-11 with cases 001/003/005 on OpenAI, 002/004/006 on Anthropic, and paced 019/020 on OpenRouter. Use this preferred 3/3/2 balance for eight-case suites:

| Provider and preferred model | Generated cases | Business assertions |
|---|---|---|
| OpenAI `gpt-5.4-mini` | `case-001`, `case-003` | Clean match and missing-receipt abstention |
| Anthropic `claude-haiku-4-5-20251001` | `case-002`, `case-004` | Partial receipt and price variance |
| OpenRouter `qwen/qwen3.8-flash` | `case-005`, `case-006`, `case-019`, `case-020` | Incorrect total, missing order, and both members of the duplicate pair |

Choose new scenarios appropriate to each demo while retaining at least two tests per provider. Calculate whole-corpus relationships before selecting provider subsets so duplicates, conflicting revisions, and linked events remain detectable. Assert deterministic business outcomes, returned source references, missing-evidence behavior, and actual provider/model identities. Disjoint workloads establish provider coverage and must not be presented as a comparative model benchmark.

Give every cloud invocation a fresh run ID so saved completions cannot satisfy a new provider qualification. Write packets, the recovery journal, `quality.json`, and JUnit `tests.xml` beneath `artifacts/<demo-name>/cloud/<run-id>/<provider>/`. Attempt the remaining providers after one fails and return a nonzero overall status for missing keys, failed provider setup, missing cases or outputs, wrong provider/model identity, failed assertions, or unexpected skips. Preserve interrupted jobs within their original run for explicit reconciliation. Keep controlled outage injection in the keyless suite. Bound completion budgets, record actual token usage including Server retries, and include model query expansion usage when configured; do not add blind client retries to paid turns.

Export a local run bundle with fixture manifests, repository revisions, image/model identities, host OS and architecture, exact commands, test counts, machine-readable test reports, logs, business outputs, and quality measurements. Fail the coordinator on a failed test, missing required dependency, unexecuted required suite, or unexpected skip. Report documented upstream chronology skips separately with their reasons; never count them as passes. Retain failed-run evidence before cleanup, and remove only resources owned by that test project. A clean-room check on each supported OS must regenerate matching fixture hashes and pass the same required assertions from a fresh checkout and empty application state; retain those reports as the evidence for reproducibility.

The earlier post-reorganization invoice run on 2026-09-10 is a successful reference: 22 unit and seven real-Server integration tests passed; 175 Python SDK unit/conformance tests passed with four documented chronology skips; all 20 generated controlled cases passed; a repeated batch reused all 20 saved responses without new completions; and eight fresh cloud cases passed across the three providers. Documentation links, public-material and license checks, and wrapper syntax checks also passed. The [invoice walkthrough](demos/invoice-exception/README.md) records commands, report locations, and limits. These results qualify Docker Desktop on Windows with Linux/AMD64 containers. Native Linux and macOS hosts and ARM64 remain pending; the other demos must record their own results.

The later invoice recheck after removing real Ollama testing passed the same controlled checks and seven of eight cloud cases. OpenRouter case-005 remained uncertain and had no recoverable completed transcript; the coordinator correctly failed and retained the journal and initial failure reports. This run does not replace the earlier successful cloud qualification. The employee policy assistant passed all six online cases in its latest run. Preserve this distinction between passed, failed, recovered, and unresolved outcomes in every demo; never count missing packets as successful cases.

Implement Wave 1 one demo at a time. For each application, create the source folder and walkthrough, implement deterministic fixtures and the complete controlled suite, then run applicable real-model profiles using the shared configuration. Verify documentation links and both entry points after any move, rerun affected tests, and record passed, failed, skipped, and pending coverage before proceeding to the next demo. Preserve existing journals and failed-run reports; make destructive volume reset an explicit operation scoped to that demo's Compose project.

Build wave 1 to establish all four language entry points. In wave 2, build records intake before the change digest, and master-data reconciliation before the shift journal. Develop the evaluation bench alongside those applications so new runbooks gain regression evidence early. Wave 3 can reuse the learned patterns without depending on another demo's runtime or datasets. Add Matrix only after the Server-only examples are independently usable.

## Munarium source references for implementation

These paths are relative to the reviewed `munarium` repository, not this demo repository. They are the implementation references to revisit when selecting the exact source revision; API names in this plan are not a replacement for the wire contract.

| Concern | Source reference |
|---|---|
| Language surface, transport gaps, installation | `clients/README.md`; `clients/{python,dotnet,java,rust}/README.md`; `clients/compatibility.json` |
| Server 1.1.1 alignment and recorded validation | `clients/docs/guides/server-1.1.1.md`; `clients/CHANGELOG.md`; `clients/check_compatibility.py` |
| Repeatable provider and routing qualification | `clients/python/conformance/compose.server-111.yaml`; `clients/python/conformance/ollama_fixture.py`; `clients/python/conformance/test_server_111.py` |
| Named providers, health, tiers, and model routing | `clients/docs/guides/providers.md` |
| Sessions, streaming, verification, transcript recovery | `clients/docs/guides/sessions.md`; `clients/python/src/munarium_client/rest_planes.py` |
| Writes, disputes, corrections, retry limits | `clients/docs/guides/write-loop.md`; `clients/docs/concepts/fact-ledger.md` |
| Historical reads and composition budgets | `clients/docs/guides/pins.md` |
| File ingestion and collection binding | `clients/docs/guides/ingest-v2.md`; `server/docs/guides/getting-started.md` |
| Capabilities, uid, reports | `clients/docs/guides/tokens-and-reports.md`; `server/docs/security-posture.md` |
| Evidence layers, completeness, explicit fact bindings | `server/docs/guides/evidence-hierarchy.md` |
| Sealed evidence access and Matrix setup | `clients/docs/guides/evidence.md`; `clients/matrix-python/README.md`; `matrix/README.md` |
| Runbooks, performance, evaluation design | `clients/docs/guides/runbooks.md`; `server/docs/guides/retrieval-sizing.md`; `server/docs/guides/creating-a-lab.md` |
| Shipped capabilities and route contracts | `server/README.md`; `server/docs/api/rest.md`; `server/docs/api/grpc.md`; `server/docs/api/errors.md` |
