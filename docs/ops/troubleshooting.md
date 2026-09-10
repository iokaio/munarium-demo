# Troubleshooting

| Symptom | Check |
|---|---|
| Web alive, readiness failing | Server `/readyz`, private DNS/network path, database readiness, configured origin |
| Page loads but no documents | Correct logical prefix, upload finalization, extraction outcomes, active index cutover |
| Some history missing | Both core and newspaper uploads; all 58 collection bindings |
| PDF/DOCX results absent | Source media type and extraction step, then rebuild/cutover the intended collection |
| Model button absent | Provider registered with a usable credential; Ollama installed model names and readiness expiry |
| Wrong expansion model | Server must be compatible with 1.1.1 selected-provider routing |
| Old question affects a new question | Start a fresh session; inspect whether the UI marked the turn as an explicit follow-up |
| Restricted sources missing | Persona clearance and compartments, not just the page title |
| Mail not received | Verified sender, provider key/delivery response, address validity; Development log-only mode |
| Login loop behind ingress | Trusted forwarding address, HTTPS scheme, browser cookie policy |
| Interrupted load | Private `.local` records and Server run state; resume the owned run rather than approving unrelated work |

Use `docker compose logs --tail 100 server demo-web` locally, but redact operational logs before sharing an issue. Never paste bearer tokens, visitor emails/codes, private deployment origins, or database connection strings. Historical experiment notes are diagnostic context, not instructions to patch a live database directly.
