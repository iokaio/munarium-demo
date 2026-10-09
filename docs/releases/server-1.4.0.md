# Web integration with Server 1.4.0

The root web stack and its restore-drill fallback select the published Server
1.4.0 image. The thirteen additional applications retain Server 1.3.0 and their
existing SDK source pins; this change does not qualify or upgrade those stacks.

| Artifact | Identity |
|---|---|
| Source | `25012c22087fbaa8ce29ddd68d2880d6e9499b1c` |
| OCI index | `sha256:800a7b19cd3e2b82a3fb8f0b33e86e3f21fc3ea71ac12a81d6a9a18a816f556a` |
| Release and qualification | [Server 1.4.0](https://github.com/iokaio/munarium/releases/tag/v1.4.0) |

Claude / Fast is selected in the browser and when either chat API omits its
provider/tier. The bundled Anthropic provider explicitly selects
`claude-haiku-5-5`, whose sampling compatibility is included in Server 1.4.0.
Explicit GPT, OpenRouter and available Ollama selections remain supported.
The OpenRouter Fast model disables reasoning through the new exact-model option.

The web stream forwards keepalives, has an absolute deadline and returns a
terminal error on timeout, interruption or EOF without an answer. Browser parsing
accepts LF and CRLF frames and completes on a terminal event even if the transport
stays open. Unknown outcomes are not automatically retried. Verification badges
name the deterministic checks and do not assert factual correctness.

## Upgrade and validation

Retain a consistent database/source/configuration backup and the current image
identity. Follow the [Server upgrade guide](https://github.com/iokaio/munarium/blob/v1.4.0/server/docs/guides/server-1.4.md)
for additive migrations 0041–0042. An image-only downgrade cannot reopen the
upgraded schema. Platform authority and external participant state require their
own recovery reconciliation; the demo does not enable platform enrollment.

Upgrade Server before applying the provider YAML. Preserve credentials, budgets,
routing and other model tiers when adapting the examples. Existing `.env` image
overrides remain effective and must be reviewed explicitly. Verify readiness,
model disclosure, fresh sessions, streaming answers and persona boundaries.

The repository's controlled web suites exercise request defaults and failure
handling without paid providers. Release-image qualification is recorded in the
upstream release; it is separate from application acceptance. A single successful
hosted turn cannot establish general model quality or qualify every corpus.
