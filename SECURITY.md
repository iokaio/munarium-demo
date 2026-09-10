# Security

Report vulnerabilities privately to info@ioka.io with "security" in the subject.
Do not submit live credentials or customer data to an issue or pull request.
Include the affected revision and a minimal reproduction against a system you
operate. Fixes target the current main branch until versioned demo releases exist.

The app handles visitor email, login-code hashes and usage counters. The browser
must never receive management or capability tokens. Review
[the security guide](docs/security.md) for operator authentication, proxy trust,
local-only defaults, and storage boundaries. Development bypass is a local
convenience, not a production configuration. A production bypass or a failure of
persona/corpus scope enforcement is a security defect.

Rotate any committed live credential immediately, then remove it from history
and artifacts. Report suspected disclosure through the private channel above.
