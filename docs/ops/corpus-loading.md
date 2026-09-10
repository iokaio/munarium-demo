# Corpus loading and recovery

The supported sequence is verify, extract, upload, build indexes, approve cutover, and verify queries. `python tools/setup.py load --provider ollama --approve` orchestrates it for the bundled data. Omit `--approve` to stop at the first pending cutover for review.

The loader preflights every logical filename against exactly one expected prefix, hashes source documents, creates a Server bulk session, uploads needed chunks, and finalizes. A completed bulk session proves storage; inspect extraction and indexing run results separately. Support's PDF and DOCX inputs exercise the converter path.

Logs and saved index-run IDs are in ignored `.local/`. An index run is scoped to the Server origin recorded with it. Do not reuse that directory against an unrelated database, even if it has the same hostname. Keep different deployments in separate checkouts/configuration directories.

For an interrupted upload, rerun loading: Server content hashes skip existing documents. To resume the exact bulk session, use `tools/corpora/loader.py --corpus <id> --resume <bulk-id>` with the tenant write token in `MUNARIUM_TOKEN`. For an interrupted index run, inspect `/v1/runs/<run-id>` before retrying. Rerunning setup resumes its saved run; `--approve` authorizes only its pending steps. A failed run requires diagnosis and a deliberately new run, not repeated arbitrary approvals.

History needs both `history` and `history-newspapers` upload layouts before building its 58 collections. The other corpus IDs are `support`, `dd`, `fin`, `patents`, and `intel`. Run `python tools/setup.py verify` after loading and test search, citations, and persona boundaries in the browser. Never use direct SQL repair commands copied from an unrelated deployment incident.

Run `python tools/verify_demo.py` for real page and persona-session-scope checks.
Add `--allow-model-calls --family ollama` to check searches and completions.
Search uses the runbook's configured expansion provider; load with `--provider ollama`
to keep the complete path local. `--family` selects the chat provider.
Cloud families require the same explicit opt-in and can incur provider charges.
The output is functional evidence, not a reproduction of historical quality scores.
