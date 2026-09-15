# Upgrade and rollback

For the 1.2 upgrade, follow the [Server 1.2 procedure](../releases/server-1.2.md),
including the automatic vocabulary generation controls and required database
restore for rollback to 1.1.

Server 1.2.1 is published. Follow its [release and integration guide](../releases/server-1.2.1.md)
for the verified digest and collection-governance behavior. The root web default
and all thirteen additional demos pin 1.2.1.
Upgrading an existing 1.2.0 database applies migration 0034; rolling back even
this patch release requires the pre-upgrade database backup, not just the old image.

Record the current web and Server image digests, source revision, runbook versions, provider configuration, and a verified backup before changing a deployment. Test the candidate in an isolated restored database with representative searches, chat, persona restrictions, and visitor login.

Server 1.1.1 fixes selected-provider routing for query expansion as well as answer generation. Its release certification includes upgrade and rollback from 1.1.0 and 1.0.0 using synthetic data. That does not certify arbitrary future schema changes or every operator's dataset.

Set `MUNARIUM_IMAGE` only to a deliberately reviewed compatible version/digest. Recreate the Server, wait for readiness, then verify active indexes and queries before changing the web image. Keep prior full-version tags and backups available. Rolling back 1.1.1 to 1.1.0 restores the older expansion routing behavior.

For a version that changes database schemas or runbook contracts, use that release's migration and rollback instructions. Do not assume an old binary can open a newly migrated database. Restore the pre-upgrade backup when an in-place rollback is unsupported.
