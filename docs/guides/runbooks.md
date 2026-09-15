# Runbooks, shapes, and providers

The twelve files under `vendor/runbooks/` contain six runbooks and six shapes. They are the versioned assets used by the demo. The import manifest preserves their source provenance. Public Server samples can illustrate the API, but are not substitutes for these corpus bindings.

`vendor/assets.json` records asset hashes. `Sync-Assets.ps1 -Check` validates them; without `-Check`, it also copies the runbooks into the web download directory. The Docker build copies the same files directly.

Apply providers before runbooks and shapes before the runbooks that refer to them. `tools/setup.py load` handles this order and rewrites the applied runbook's `provider: default` bindings to the selected `demo-*` provider. The source YAML remains available unchanged for inspection. Use one provider choice consistently when resuming a bootstrap.

Index runs have explicit cutover steps. Upload completion alone does not activate a searchable collection. Inspect a run and approve only the intended cutovers; the bootstrap's `--approve` applies to its own saved run IDs. The [loading guide](../ops/corpus-loading.md) describes resuming.

If you edit assets, update their checksums and provenance and review the change in a signed-off commit. Existing Server configurations are versioned: use a new runbook version for a changed deployment rather than treating a version number as mutable configuration.

On Server 1.2, session retrieval can apply collection vocabularies in addition
to runbook retrieval settings. Vocabulary generation/editing is a separate
Server API, not a change to these bundled YAML files. Review its default-on
generation and provider costs when upgrading.

Server 1.2.1 publication snapshots and collection queries are also separate from
runbook versions and cutover steps. The current loader does not create those
snapshots, and the web app continues to query sessions. Adopting the new path
requires deliberate publication bindings, model/processing policy and original
source mappings; see [1.2.1 integration](../releases/server-1.2.1.md).
