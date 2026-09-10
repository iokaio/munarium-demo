# Local quickstart

Use Docker Engine/Desktop with Compose and Python 3.11+. The Linux Server image supports AMD64 and ARM64; the release's ARM64 certification ran under emulation. Commands run from this repository, with no adjacent checkout or cloud login.

```console
python -m pip install -r tools/requirements.txt
python tools/setup.py init
python tools/setup.py start --provider ollama
python tools/setup.py load --corpus support --provider ollama --approve
python tools/setup.py verify --corpus support
```

Open <http://localhost:5310/support>. Initialization creates ignored `.env` with
six independent random secrets only when that file does not already exist. If
it exists, `init` leaves it unchanged, including any missing or empty settings.
Starting creates PostgreSQL, Server 1.1.1, Ollama, and the web app, then downloads
the completion and embedding models. Loading verifies source hashes, applies
the provider and shapes, binds runbook default models to your selected provider,
uploads documents, and builds indexes. `--approve` authorizes cutover only for
runs recorded by this checkout.

`verify --corpus support` checks the support runbook, populated active indexes
and document count. To check its actual page, retrieval and local-model answer:

```console
python tools/verify_demo.py --corpus support --allow-model-calls --family ollama
```

Omitting `--allow-model-calls` checks pages and applicable persona session scopes;
it does not test search or completion. Search can itself invoke model-based query
expansion. After loading all six corpora, omit `--corpus support` to check all six.

Load all six workspaces when ready:

```console
python tools/setup.py load --provider ollama --approve
python tools/setup.py verify
```

The default loopback ports are 5310 for the browser and 8080 for Server. Set `DEMO_HOST_PORT` and `SERVER_HOST_PORT` in `.env` if occupied. The supplied local gate bypass is restricted to Development. See [hosting](../ops/deployment.md) for external access.

With a custom browser port, pass the matching `--base-url` to `verify_demo.py`.
Its corpus names follow browser routes; setup names follow the data manifest.
See the [corpus-name mapping](corpora.md#corpus-names-and-browser-routes).

A complete bundled-data load and index build took approximately 32 minutes on
Windows with Docker Desktop allocated 12 CPUs and 32 GB RAM, using fresh database
and visitor volumes. All 70,415 source documents and 104 active collections matched
the manifest. Model downloads and image building are additional; hardware and disk
performance can change the time substantially. The database occupied about 3.5 GiB
after the full functional checks; reserve additional space for images, models,
extracted documents, backups, and a restore rehearsal. The support-only profile is the
smaller first-run option.

For a cloud provider, set its `MUNARIUM_SECRET_*` value privately in `.env`, then use the same `--provider anthropic`, `openai`, or `openrouter` argument for start and load. Model requests can incur charges. Inspect the provider YAML's model IDs and budgets before choosing it.

Stop with `docker compose --profile local-model stop`. Restart with the start command. Named volumes retain state; removing them destroys that installation's data. [Loading recovery](../ops/corpus-loading.md) explains interrupted runs.
