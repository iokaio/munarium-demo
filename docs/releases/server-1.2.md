# Munarium Server 1.2 upgrade

Server 1.2.0 adds collection vocabularies and checked answer composition. The
demo web application continues to use its backend HTTP client and existing
session API; installing the new Server does not add a vocabulary editor to the
visitor UI. Vocabulary administration belongs to a trusted backend/operator.

## API and client compatibility

All documented Server REST operations have named native RPCs on
`mmp.v1.ServerApiService`. The Rust, Python, .NET and Java Server client packages
1.1.0 expose them through `ServerApiClient`; Python also has an async client.
Existing typed client interfaces remain compatible. The complete API clients
require Server 1.2 for their new gRPC methods. Additional demo applications keep
their own pinned SDK revisions until those applications are separately updated
and tested; a web deployment does not update every example automatically.

See the [Server client guide](https://github.com/iokaio/munarium/blob/main/clients/docs/guides/server-1.2.md)
for payload representation, streaming, error limits and language examples, and
the [Server vocabulary guide](https://github.com/iokaio/munarium/blob/main/server/docs/guides/collection-vocabularies.md)
for the normative API and scope requirements.

## Vocabulary defaults and cost

Automatic generation is enabled by default. Server samples up to 12 documents,
balances document types and uses bounded excerpts. Global settings control
automatic generation, sampling, provider and model tier. Each collection can
inherit or override automatic generation and sampling, disable vocabulary use,
replace or edit its groups, and refresh from selected source IDs. Updates use
revisions so concurrent edits cannot silently overwrite each other.

An upgrade can make existing eligible collections candidates for generation.
Generation uses the Server's configured model provider and can incur model
charges. Review global defaults, provider credentials and eligible collections
before exposing the upgraded backend to normal traffic. Preserve manually
maintained vocabularies. Sampled documents remain untrusted model input; a
vocabulary cannot grant access to additional collections or source files.

## Answers and original files

The new answer API returns a model-generated narrative and verified citation
references. References identify the indexed source, content hash, chunk and
index version. New indexes can carry extraction locations; old indexes may
not have page or offset information. Rebuilding creates a new immutable index;
it does not rewrite a previously pinned answer.

The ingesting application maps references to its original files, checks current
authorization and serves them. Munarium does not turn a citation into a download
URL or serve the application's files. Keep the source ID and indexed hash from
ingest instead of relying on a display title. A shared answer or source URL must
not bypass the application's current admission and scope checks.

## Upgrade and rollback

1. Record current image digests, provider/runbook settings, active indexes and
   both REST/gRPC deployment identities. Back up the Server database and the
   web application's persistent visitor state; verify a restore in isolation.
2. Select the published 1.2.0 image by immutable digest from the
   [compatibility record](README.md). Use the same digest for REST and gRPC.
   Preserve secrets, identities, mounts, database configuration and storage.
3. Start Server and verify version, readiness, existing data and both API
   transports. Additive migrations 0032 and 0033 run at startup. Check vocabulary
   controls before allowing automatic generation against cloud providers.
4. Deploy the compatible demo web image. Test visitor login, existing corpora,
   persona restrictions, streaming, source links and persistence across restart.
   Record actual results and image identities before calling the deployment ready.

**Rollback requires the pre-upgrade database backup.** Do not start Server 1.1
against a database containing the 1.2 migrations. Stop writes, restore the
matching backup and prior image/configuration, and verify both transports and
the web application. Restoring a backup discards writes made after that backup;
keep the failed candidate's database for reconciliation before restoring.

Release and deployment verification results belong in the
[compatibility record](README.md). This procedure does not itself certify a
particular image or installation.
