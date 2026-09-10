# Standalone deployment validation

Validation performed on 2026-09-10 UTC with the published Munarium Server 1.1.1,
the bundled corpus packs and runbooks, PostgreSQL source storage, and local Ollama.
The database and visitor volumes started empty. No cloud-provider key, Azure
login, private registry, or data acquisition step was needed for this installation.

| Check | Result |
|---|---|
| Complete input inventory | 30 packs; 70,415 documents; hashes verified |
| Source extraction | All 70,415 successful, including 30 PDFs and 40 DOCX files |
| Active indexes | 104 populated collections across all six runbooks |
| Idempotent upload | Support rerun skipped all 1,500 existing documents |
| Functional application | All six real pages, searches, evidence-bearing local-model answers |
| Data-room isolation | Associate, counsel and clean-team session scopes, search and chat |
| Access controls | Production secret checks, no Development bypass in Production, disabled console defaults, operator isolation |
| Visitor persistence | Manual code issuance without mail, login, code reuse after restart, blocking |
| Database restore | Full dump restored into a separate PostgreSQL/Server pair; all source counts and active indexes verified |
| Disclosure checks | Public source and nested archives; private deployment inventory; image configuration and all exported layer files |
| Contribution checks | Locked build, formatting, browser/HTTP regressions, licenses/notices, documentation links, workflow/PowerShell lint, CI deployment boundary |

The full load/index cycle took approximately 32 minutes with Docker Desktop
allocated 12 CPUs and 32 GB RAM on Windows. The resulting database was about
3.5 GiB. Model evaluation takes additional time. These measurements describe
this environment and are not a performance guarantee.

Functional model checks used `qwen3:1.7b`. They verify working source retrieval,
answers and access boundaries, not the historical quality scores recorded in
experiment notes. Operator-owned Azure infrastructure, live email delivery and
paid cloud providers require their own deployment validation.

Private dumps, generated credentials, detailed operational receipts and the old
repository history are excluded from the publication tree. Follow the indexed
[operations guides](../ops/README.md) for your own installation and preserve the
[data rights](../../data/RIGHTS.md) and [third-party notices](../../THIRD_PARTY_NOTICES.md).
