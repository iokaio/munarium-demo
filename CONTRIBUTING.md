# Contributing to Munarium Demo

Contributions are accepted under Apache-2.0. There is no CLA. Every commit must
carry a Developer Certificate of Origin sign-off (`git commit -s`): see
https://developercertificate.org/.

Fork, create a topic branch, make the change, run the checks below, and open a
pull request to main. Explain the problem, changed behavior, and validation.
Disclose third-party code and licenses, generated code and inputs, AI tools used
and your review, and any employer or contractual restrictions. You must have the
right to submit every file.

```console
python -m pip install -r tools/requirements.txt
python tools/check_public.py
python tools/check_license.py
python tools/check_docs.py
pwsh ./Sync-Assets.ps1 -Check
python tools/corpora/unpack.py --verify-only
dotnet build Demo.sln -c Release -warnaserror
dotnet format Demo.sln --verify-no-changes
npm ci
npx playwright install chromium
node tools/test-readiness.cjs
node tools/test-chat-context.cjs
node tools/test-ollama.cjs
node tools/test-workspace.cjs
node tools/test-public-config.cjs
```

Run PowerShell analysis and the CI boundary self-test as documented in
[development](docs/guides/development.md). CI uses no deployment credentials or
paid providers. Source files have Apache-2.0 SPDX headers; JSON and binary
licenses are recorded in the repository license inventory.

The sole-maintainer model uses maintainer self-review, required checks, linear
history and resolved conversations. CODEOWNERS identifies the responsible
maintainer; this does not claim independent review of that maintainer's own PR.
A second eligible maintainer can enable required code-owner approval.

Only maintainer-authored changes may alter LICENSE, NOTICE, TRADEMARK.md,
CONTRIBUTING.md, SECURITY.md, SUPPORT.md, CODE_OF_CONDUCT.md, .github/, licensing
inventories, publication scanners, or signing/release configuration. Changes to
these controls require an explicit review of their effect on the release boundary.

Update the indexed documentation with behavior changes. Never contribute live
endpoints, keys, visitor records, or infrastructure state. Dataset changes need
provenance, rights, hashes, and updated questions. Do not ingest answer keys.
See [conduct](CODE_OF_CONDUCT.md) and [security reporting](SECURITY.md).
