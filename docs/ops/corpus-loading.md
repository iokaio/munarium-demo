# Corpus loading and recovery

The supported sequence is verify input hashes, extract, upload, build indexes,
approve cutover, and verify the application. `python tools/setup.py load --provider
ollama --approve` orchestrates the steps through cutover for bundled data. Run
`setup.py verify` for collection/count checks and `verify_demo.py` for functional
checks as described below. Omit `--approve` to stop at the first pending cutover
for review.

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

The verification tool uses browser corpus IDs (`revolution`, `dataroom`,
`advisory`, etc.), while setup uses manifest IDs (`history`, `dd`, `fin`, etc.).
It also uses model families `claude` and `gpt` where setup uses providers
`anthropic` and `openai`. See [the corpus mapping](../guides/corpora.md#corpus-names-and-browser-routes).
For a support-only installation:

```console
python tools/setup.py verify --corpus support
python tools/verify_demo.py --corpus support --allow-model-calls --family ollama
```

`verify_demo.py` defaults to `http://127.0.0.1:5310`; pass `--base-url` for other
ports or hosts. For a gated installation, supply an authorized visitor cookie
privately in `DEMO_VERIFY_COOKIE`. Completed run IDs remain in `.local/`; rerunning
setup reuses them rather than automatically creating a new indexing run for
changed assets. Plan an explicit new run when rebuilding changed content.
