# Development

Install .NET SDK 10, Node.js 22+, Python 3.11+, and PowerShell 7. Docker is required for image and full-stack checks. No cloud credentials are required for the controlled regression suites.

```console
python -m pip install -r tools/requirements.txt
npm ci
npx playwright install chromium
pwsh ./Sync-Assets.ps1
dotnet build Demo.sln -c Release -warnaserror
dotnet format Demo.sln --verify-no-changes --no-restore
node tools/test-readiness.cjs
node tools/test-chat-context.cjs
node tools/test-ollama.cjs
node tools/test-workspace.cjs
node tools/test-public-config.cjs
```

The tests launch uniquely scoped local web processes and controlled backends. Browser tests use the pinned Playwright Chromium. Linux CI installs its system dependencies with `npx playwright install --with-deps chromium`.

Run `pwsh ./run-local.ps1` to develop against a Server you started separately. Set `MUNARIUM_BASE_URL` and its tenant management token in the process environment. Use `Gate__Disabled=true` only for local Development. The script verifies and copies the repository's own runbooks; no other repository is read.

Follow [CONTRIBUTING.md](../../CONTRIBUTING.md) for DCO sign-off and required disclosures. Keep generated SQLite files, downloaded models, extracted corpora, build outputs, and credentials ignored. Use [the documentation index](../README.md) for operation-specific checks.
