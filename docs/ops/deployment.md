# Hosting the demo

The supplied Compose stack is a local installation with loopback ports and a Development gate bypass. For an externally reachable host, create an operator-owned override with Production mode, gate bypass disabled, TLS ingress, persistent volumes, and private Server/database networking.

Generate distinct strong secrets and deliver them using your platform's secret store. Configure `DEMO_GATE_SECRET`, Server management/capability secrets, operator credentials, a persistent `DEMO_STORE_PATH`, and a verified mail sender. Set `DEMO_TRUSTED_PROXIES` to the actual trusted ingress IP addresses so forwarded scheme/IP handling works without trusting arbitrary clients.

Use one web replica. Mount the visitor store and keep PostgreSQL and source storage durable. Use `/livez` for liveness and `/readyz` for readiness; a sleeping/unreachable backend must not cause the web app to restart continuously. Restrict management access and keep the optional operator-console proxy disabled unless you intend to use it.

Build the web image from this repository with `docker build -t munarium-demo-web:local .`. The image excludes corpus packs, credentials, visitor stores, and private configuration. Load corpora from a trusted operator machine using the bundled loader; do not bake write credentials into an image.

After deployment, verify the gate from a fresh browser, issue and reuse a visitor code, check admin authentication, load every page, and test search/chat/citations and restricted personas. Rehearse [backup/restore](backup-restore.md) and [upgrade/rollback](upgrade-rollback.md) before a public demonstration.
