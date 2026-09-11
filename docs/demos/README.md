# Demo applications

Each new demo has its own code, Docker setup, and tests under `src/<demo-name>/`. Each `docs/demos/<demo-name>/` folder contains a `README.md` walkthrough, recorded validation, and a linked `application.png` rendering. Every walkthrough begins with a business case explaining why a company might build a similar solution.

- [Invoice exception batch](invoice-exception/README.md): Python CLI with synthetic invoices, deterministic checks, cited review packets, and tests across three AI providers. Source: [src/invoice-exception](../../src/invoice-exception).
- [Employee policy assistant](employee-policy-assistant/README.md): C# Avalonia desktop with identity-scoped policy answers, real streaming progress, source inspection, and local Docker tests. Source: [src/employee-policy-assistant](../../src/employee-policy-assistant).
- [Order exception triage](order-exception-triage/README.md): Java queue consumer with a durable inbox, cited routing proposals, duplicate-event handling, and transcript recovery. Source: [src/order-exception-triage](../../src/order-exception-triage).
- [Maintenance procedure terminal](maintenance-terminal/README.md): Rust terminal with explicit revision selection, retrieval-only operation, source inspection and optional cited explanations. Source: [src/maintenance-terminal](../../src/maintenance-terminal).
- [Records intake service](records-intake/README.md): C# background worker with stable-file reconciliation, explicit binding and separately approved index activation. Source: [src/records-intake](../../src/records-intake).
- [Master-data reconciliation](master-data-reconciliation/README.md): Java CLI with cited stewardship drafts, explicit reviews, retained disputes, corrections and historical fact reads. Source: [src/master-data-reconciliation](../../src/master-data-reconciliation).
- [Shift handover journal](shift-handover/README.md): Rust daemon and CLI with reviewed events, durable write receipts, commitments and consistently pinned historical briefs. Source: [src/shift-handover](../../src/shift-handover).
- [Policy change impact digest](policy-change-digest/README.md): Python scheduled job with explicit policy revisions, textual diffs, cited candidate impacts and durable pair checkpoints. Source: [src/policy-change-digest](../../src/policy-change-digest).
- [Retrieval evaluation bench](retrieval-evaluation/README.md): Python CLI with independent source labels, separate access profiles, controlled setting comparisons, repeated completions and measured plots. Source: [src/retrieval-evaluation](../../src/retrieval-evaluation).
- [Planned demos](../other-demo-plans.md): remaining non-web enterprise scenarios and implementation guidance.
