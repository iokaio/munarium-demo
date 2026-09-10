# Upgrade and rollback

Record the current web and Server image digests, source revision, runbook versions, provider configuration, and a verified backup before changing a deployment. Test the candidate in an isolated restored database with representative searches, chat, persona restrictions, and visitor login.

Server 1.1.1 fixes selected-provider routing for query expansion as well as answer generation. Its release certification includes upgrade and rollback from 1.1.0 and 1.0.0 using synthetic data. That does not certify arbitrary future schema changes or every operator's dataset.

Set `MUNARIUM_IMAGE` only to a deliberately reviewed compatible version/digest. Recreate the Server, wait for readiness, then verify active indexes and queries before changing the web image. Keep prior full-version tags and backups available. Rolling back 1.1.1 to 1.1.0 restores the older expansion routing behavior.

For a version that changes database schemas or runbook contracts, use that release's migration and rollback instructions. Do not assume an old binary can open a newly migrated database. Restore the pre-upgrade backup when an in-place rollback is unsupported.
