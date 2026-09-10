# Backup and restore

Back up the Server database, source storage, visitor SQLite database, applied runbook/provider configuration, and matching secrets. The default local stack keeps both raw sources and Server state in PostgreSQL; deployments using external source storage must back up that storage separately.

Use PostgreSQL's supported logical or physical backup tools. Quiesce writers and stop the web app before copying its SQLite database and any WAL/SHM companions; copying an actively changing SQLite file is not a consistent backup. Encrypt backups and keep them outside this repository. Model files can be downloaded again; corpus packs are reconstructible from the manifest.

Restore into a separate isolated deployment with the same compatible image first. Restore the matching signing secrets privately, verify `/readyz`, reload provider configuration if needed, and confirm active indexes, existing sessions, source retrieval, visitor code reuse, and blocking records. Rebuild indexes through runbooks only if inspection shows they are absent or invalid.

Document your actual backup commands, storage destination, retention, and restore evidence in private operations records. Do not commit a database dump, visitor registry, secret export, or cloud storage URL as an example.

For the supplied local Compose layout, `python tools/restore_drill.py --project
munarium-demo` takes a consistent PostgreSQL dump and verifies it in a new isolated
database and Server. Supply your actual Compose project name if different. The
drill checks all source counts and active indexes, removes only its labelled test
containers/network, and retains its dump and evidence privately under `.local/`.
It does not replace the separate visitor SQLite backup or restore your provider
credentials. No model endpoint is required for its database/index checks.
