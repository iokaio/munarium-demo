# Troubleshooting

| Symptom | Check |
|---|---|
| Web alive, readiness failing | Server `/readyz`, private DNS/network path, database readiness, configured origin |
| Page loads but no documents | Correct logical prefix, upload finalization, extraction outcomes, active index cutover |
| Some history missing | Both core and newspaper uploads; all 58 collection bindings |
| PDF/DOCX results absent | Source media type and extraction step, then rebuild/cutover the intended collection |
| Model button absent | Check `DEMO_FAST_ONLY`, provider registration and credential availability; for Ollama, check installed model names and readiness expiry |
| Wrong expansion model | Server must be compatible with 1.1.1 selected-provider routing |
| `model-budget` after retrieval | The selected provider/tier cannot reserve the next completion within its daily token cap. Choose another available model, or wait until midnight UTC. Operators should inspect `/v1/reports/budgets`; automatic vocabulary generation also consumes provider tokens. |
| `rate-limited` | The provider's short rate window is full; wait a minute before retrying. This differs from the daily spending cap. |
| Old question affects a new question | Start a fresh session; inspect whether the UI marked the turn as an explicit follow-up |
| Restricted sources missing | Persona clearance and compartments, not just the page title |
| Mail not received | Verified sender, provider key/delivery response, address validity; Development log-only mode |
| Login loop behind ingress | Trusted forwarding address, HTTPS scheme, browser cookie policy |
| Interrupted load | Private `.local` records and Server run state; resume the owned run rather than approving unrelated work |
| `init` reports an existing `.env`, but Compose reports a missing setting | Initialization does not repair an existing file; compare its keys with `.env.example` and supply missing private values |
| `verify` fails after loading only support | Use `setup.py verify --corpus support`; the default checks all six corpora |
| Verification rejects `history`, `dd` or `fin` | `verify_demo.py` uses browser IDs `revolution`, `dataroom`, `advisory`; setup uses manifest IDs |
| A setting in `.env` has no effect on the web app | Add it to the container's `environment:` configuration; Compose does not forward arbitrary `.env` entries |
| Server or restore drill rejects migration 0034 | The database has reached 1.2.1. Use its matching image, or restore the pre-upgrade backup before starting 1.2.0; the drill defaults to 1.2.1 unless `MUNARIUM_IMAGE` is set |
| No vocabulary/governance editor after upgrading Server | The web adapter still uses `/v1` sessions; the new Server APIs need a separate authorized integration |
| Collection-query or publication route is unavailable | Verify `/version` is 1.2.1; all demo defaults pin 1.2.1, but an existing image override or running container can retain an older version |

Use the [1.2.1 integration and acceptance guide](../releases/server-1.2.1.md)
for the exact image, API requirements and restore procedure.

Use `docker compose logs --tail 100 server demo-web` locally, but redact operational logs before sharing an issue. Never paste bearer tokens, visitor emails/codes, private deployment origins, or database connection strings. Historical experiment notes are diagnostic context, not instructions to patch a live database directly.
