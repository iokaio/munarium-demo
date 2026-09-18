# Development

Install .NET SDK 10, Node.js 22+, Python 3.11+, and PowerShell 7. Docker is required for image and full-stack checks. No cloud credentials are required for the controlled regression suites.

```console
python -m pip install -r tools/requirements.txt
npm ci
npx playwright install chromium
pwsh ./Sync-Assets.ps1
dotnet restore Demo.sln --locked-mode
dotnet build Demo.sln -c Release -warnaserror --no-restore
dotnet format Demo.sln --verify-no-changes --no-restore
node tools/test-readiness.cjs
node tools/test-chat-context.cjs
node tools/test-ollama.cjs
node tools/test-workspace.cjs
node tools/test-public-config.cjs
```

The tests launch uniquely scoped local web processes and controlled backends. Browser tests use the pinned Playwright Chromium. Linux CI installs its system dependencies with `npx playwright install --with-deps chromium`.

For documentation and bundled-asset changes, run the corresponding repository
checks from the root:

```console
python tools/check_docs.py
python tools/check_public.py
python tools/check_license.py
pwsh ./Sync-Assets.ps1 -Check
python tools/corpora/unpack.py --verify-only
python tools/corpora/emit_history_yaml.py --check
```

For PowerShell changes, use the same analyzer version and settings as CI:

```powershell
Install-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -Scope CurrentUser
Import-Module PSScriptAnalyzer -RequiredVersion 1.24.0
$scripts = Get-ChildItem -Path . -Recurse -File -Include *.ps1, *.psm1, *.psd1 |
    Where-Object { $_.FullName -notmatch '(\\|/)(bin|obj|vendor|node_modules|data)(\\|/)' }
$findings = @($scripts | Invoke-ScriptAnalyzer -Settings ./PSScriptAnalyzerSettings.psd1)
$findings | Format-Table RuleName, Severity, ScriptName, Line, Message
if ($findings.Count) { throw 'PowerShell analysis found issues.' }
./tools/check_ci_boundary.ps1 -SelfTest
./tools/check_ci_boundary.ps1
```

Run `pwsh ./run-local.ps1` to develop against a Server you started separately. Set `MUNARIUM_BASE_URL` and its tenant management token in the process environment. Use `Gate__Disabled=true` only for local Development. The script verifies and copies the repository's own runbooks; no other repository is read.

Follow [CONTRIBUTING.md](../../CONTRIBUTING.md) for DCO sign-off and required disclosures. Keep generated SQLite files, downloaded models, extracted corpora, build outputs, and credentials ignored. Use [the documentation index](../README.md) for operation-specific checks.
