# Local models

`python tools/setup.py start --provider ollama` starts the optional Compose profile and downloads `qwen3:1.7b` for completion and `all-minilm:22m` for embedding. The model volume is local to this deployment. Cloud-provider keys are not required for this path.

The embedding model is available through the Server provider API. Server 1.1.1
index builds still use the built-in local embedder; downloading or changing
`all-minilm:22m` does not change the corpus index embeddings. Ollama model tags
are pulled by name by `setup.py`; this bootstrap does not verify model digests.

The demo uses direct readiness mode: it calls Ollama's `/api/tags` from the web backend and checks that the configured fast/capable models are installed. Its short-lived availability response contains model IDs and expiry, not an endpoint or credential. The Server provider YAML uses the Compose endpoint and matching tier IDs.

A CPU-only local model may respond slowly. Configure your own GPU access if available; do not assume the Compose profile provisions a GPU. A small local model demonstrates the protocol and grounding flow, but may not reproduce larger-model historical quality scores.

If using an authenticated readiness gateway, omit direct mode and set the gateway URL/key privately. Its `/readyz` JSON must contain `ready`, future `expiresAt`, and `fast`/`capable` values matching the Server provider catalog. The browser never receives the gateway key. Expired or failed readiness hides Ollama availability.

Gateway mode requires a nonempty key and HTTPS, except for HTTP on loopback.
Readiness expiry must be no more than three hours and one minute in the future.
Direct mode calls `/api/tags` without a bearer key and advertises availability
for 45 seconds when both configured model names are present.
