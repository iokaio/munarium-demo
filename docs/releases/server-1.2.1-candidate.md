# Server 1.2.1 candidate validation

Server 1.2.1 is an **unpublished candidate**. The demo's default and documented
online baseline remain [published Server 1.2.0](README.md). Do not substitute a
candidate test result for release qualification or an operator's acceptance of
a deployed installation.

## Changed Server behavior

The [Server draft PR](https://github.com/iokaio/munarium/pull/28) adds
`POST /v1.2/query`: a caller supplies the question, collection scope and optional
governing date, with clearance carried by its capability. Server selects the
authorized governing publications, applies collection vocabulary, retrieves
passages and asks the configured model to explain what the files say. The
application displays that explanation and the returned references. Keyword
phrases use the same path as full questions; applications need no topic dictionary
or domain-specific question rewrite.

An incomplete answer can explain what is present and what is missing. A result
requiring review can explain the available content and uncertainty without
claiming to resolve conflicting rules. Verified references accompany substantive
claims. Applications should preserve this narrative instead of displaying each
quotation as a separate answer or discarding explanations based on status alone.

The candidate also adds collection-governance GET/PUT and original-publication
authorization. Published identities, hashes and index pins remain stable;
withdrawal and governing-version decisions stay in Server. The ingesting
application retains its source mapping, enforces current user admission and
serves originals. A Server reference is not a file download URL.

The complete API inventory contains 121 named operations over REST and native
gRPC in the Rust, Python, .NET and Java clients. These additions do not force
existing session-based applications onto the collection-query API. This demo's
web application still uses its existing session client; the additional example
applications retain their independently pinned SDK and Server revisions.

## Operator qualification

Before promoting a candidate, record its immutable source revision and image
digest and complete these checks against those exact bytes:

1. Pass source CI, client REST/gRPC conformance, migration checks and fresh
   dependency/image scans. Report skipped or unavailable checks separately.
2. Back up the Server database, configuration, index artifacts and persistent web
   state. Restore the backup into an isolated installation and verify the prior
   image before changing the online services. Candidate migration 0034 is
   additive; an image-only rollback to 1.2.0 is not a recovery procedure.
3. Preserve configured model credentials and vocabulary preferences. Check
   sampling defaults and hosted-model costs before eligible collections can
   generate vocabularies. Exercise PostgreSQL and enabled Datastore paths,
   existing indexes, original references and restart persistence.
4. Use the same qualified digest for REST and gRPC services. Validate the web
   demo's visitor/operator admission, persona scope, existing corpora, streamed
   explanations, citations and browser navigation. A healthy process alone
   does not establish answer quality.
5. Record observed results and remaining limitations, obtain manual acceptance,
   and only then authorize release publication and the default-image change.

Use [upgrade and rollback](../ops/upgrade-rollback.md) and
[backup and restore](../ops/backup-restore.md) for deployment procedures. Keep
credentials, private deployment inventories and visitor data outside this
public repository. This page records the required transition; it does not claim
that the online demo has been upgraded or that candidate acceptance has passed.
