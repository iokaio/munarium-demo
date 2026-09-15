# Server 1.2.1 candidate transition record

This page was first written on 2026-09-14 while Server 1.2.1 was an unpublished
candidate. Server PR #28 has since merged and **1.2.1 is published**. Use the
[current 1.2.1 release and integration guide](server-1.2.1.md) for artifact
identities, API behavior and upgrade/rollback requirements.

The candidate checklist called for source/client CI, image and dependency
scans, database backup/restore, both transport identities, vocabulary/provider
policy review, original-file mapping, and application acceptance. The
[upstream publication record](https://github.com/iokaio/munarium/blob/main/server/CONTAINER.md#versions-and-verification)
now records Server release qualification, including the synthetic 1.2.0 to
1.2.1 upgrade and database-restore rollback.

This checkout now pins Server 1.2.1 for all fourteen stacks and current official
clients for the thirteen additional applications. See [compatibility](README.md)
for their local qualification and the separate recorded online 1.2.1 deployment.
That deployment passed thirteen free browser checks; no fresh hosted-provider
streaming answer is claimed.

Before changing an installation or its default image, follow the current
[1.2.1 acceptance procedure](server-1.2.1.md#web-stack-upgrade-and-acceptance),
[upgrade/rollback](../ops/upgrade-rollback.md) and
[backup/restore](../ops/backup-restore.md) guides. Preserve credentials, visitor
data and deployment evidence outside this public repository.
