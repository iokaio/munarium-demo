# Security boundaries

The public configuration contains local defaults. Each operator supplies deployment origins, signing keys, database credentials, provider keys, and email settings privately. The web app keeps management and capability tokens out of browser responses.

Visitor access uses an email and reusable generated login code. The database retains a keyed hash of the code, and the browser receives an HMAC-signed pseudonymous cookie. That cookie expires at the next UTC midnight; an optional shorter age ceiling also applies. Email remains in the web app's SQLite registry and is delivered to the configured mail provider when sending a code.

`/admin` has separate operator credentials, a 30-minute signed cookie, and antiforgery validation for state-changing forms. The optional upstream console proxies are disabled by default and require operator authentication. Visitor admission alone does not authorize them.

Collection clearances and compartments are enforced by Munarium Server. UI persona controls are a demonstration of that contract; hiding a button is not an authorization boundary. Keep the management endpoint and database on private networks.

Every admitted visitor can select any configured data-room persona, including
the clean team. The backend mints the scope for that selection; it does not map
the visitor's identity to an organization-approved role. To adapt this demo for
confidential business data, derive the permitted persona and clearance from your
own authenticated authorization policy before minting a capability.

Run one web replica. Turn counters, failed-login counters, and global revocation cutoff are in memory and reset on restart. Blocking records and code hashes persist in SQLite. Rotating the gate secret invalidates existing cookies, code hashes, and pseudonymous IDs, so plan reissuance and attribution changes.

Use TLS and explicitly trusted ingress addresses in Production. Development can log delivery codes and bypass visitor admission; it is intended for loopback use. Set provider budgets and appropriate data retention. Follow [SECURITY.md](../SECURITY.md) for private vulnerability reporting.
