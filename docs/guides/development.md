# Development

Install .NET SDK 10, Node.js 22+, Python 3.11+, and PowerShell 7. Docker is required for image and full-stack checks. No cloud credentials are required for the controlled regression suites.

Run affected checks locally before pushing and report commands/results in the PR.
Automatic CI builds and formats the .NET app, analyzes PowerShell, lints workflows,
and checks deployment boundaries and repository hygiene. Browser regressions and
the Docker build run only on manual `demo-ci` dispatch. A green automatic check
does not replace local browser or packaging validation. See the tracked
[AGENTS.md](../../AGENTS.md) and [CLAUDE.md](../../CLAUDE.md) instructions.

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

The tests launch uniquely scoped local web processes and controlled backends. Browser tests use the pinned Playwright Chromium. Manually dispatched Linux CI installs its system dependencies with `npx playwright install --with-deps chromium`.

For Dockerfile or packaging changes, also run `docker build -t munarium-demo-web:local-check .`
from the repository root. This builds the image without publishing it. For hosted
reproduction, select Actions > demo-ci > Run workflow for the reviewed branch, or
run `gh workflow run demo-ci.yml --ref <branch>`. Avoid dispatching the full suite
after every edit; fix failures locally and batch related changes.

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
$scripts = @(git ls-files -- '*.ps1' '*.psm1' '*.psd1' |
    Where-Object { $_ -notmatch '(^|/)(bin|obj|vendor|node_modules|data)/' } |
    ForEach-Object { Get-Item -LiteralPath $_ })
$findings = @($scripts | Invoke-ScriptAnalyzer -Settings ./PSScriptAnalyzerSettings.psd1)
$findings | Format-Table RuleName, Severity, ScriptName, Line, Message
if ($findings.Count) { throw 'PowerShell analysis found issues.' }
./tools/check_ci_boundary.ps1 -SelfTest
./tools/check_ci_boundary.ps1
```

The analyzer inventory matches a clean checkout. Analyze new, unstaged scripts
explicitly too, or stage their intended paths before running this command.

Run `pwsh ./run-local.ps1` to develop against a Server you started separately. Set `MUNARIUM_BASE_URL` and its tenant management token in the process environment. Use `Gate__Disabled=true` only for local Development. The script verifies and copies the repository's own runbooks; no other repository is read.

Follow [CONTRIBUTING.md](../../CONTRIBUTING.md) for DCO sign-off and required disclosures. Keep generated SQLite files, downloaded models, extracted corpora, build outputs, and credentials ignored. Use [the documentation index](../README.md) for operation-specific checks.
