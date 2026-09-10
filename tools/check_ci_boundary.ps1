# SPDX-License-Identifier: Apache-2.0
#Requires -Version 7
<#
.SYNOPSIS
Refuse any GitHub workflow in this repository that could deploy the demo.

.DESCRIPTION
Rolling the demo image onto Ioka's estate is a local PowerShell act, an
operator running deploy.ps1, and never a CI one. CI here builds and lints.
It holds no Azure identity: no federated credential is subjected to
repo:iokaio/munarium-demo, and demo-ci grants itself nothing beyond
contents: read. Those are facts about Azure and about one workflow file
today, not properties of the repository. This gate makes them properties: a
workflow that logs in to Azure, requests an OIDC token, reads a secret, calls
the Azure CLI, pushes an image or names the registry, or invokes deploy.ps1
fails demo-ci, and cannot merge without editing this file in the same change.

Scope is .github/workflows and .github/actions, the complete enumeration of
what GitHub executes. Comments are stripped and shell continuations joined
before matching, so a workflow may explain the rule in prose without tripping
it, and a directive hidden behind a '#' could not execute anyway.

.PARAMETER SelfTest
Run the rules against built-in fixtures instead of the repository. Every
denied fixture must be refused and every allowed one must pass. demo-ci runs
both modes, so a rule that quietly stops matching fails the build.
#>
[CmdletBinding()]
param([switch]$SelfTest)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$rules = @(
    @{ Name = 'azure-login'
       Pattern = '(?i)azure/login|(?<![\w.-])az\s+login\b|Connect-AzAccount'
       Why = 'CI holds no Azure identity' }
    @{ Name = 'oidc-token'
       Pattern = '(?i)id-token\s*:\s*write'
       Why = 'no workflow may request an OIDC token' }
    @{ Name = 'secret'
       Pattern = '(?i)secrets\.(?!GITHUB_TOKEN\b)\w+'
       Why = 'no workflow reads a repository or environment secret' }
    @{ Name = 'azure-cli'
       Pattern = '(?<![\w.-])az\s+[a-z]'
       Why = 'the estate is changed by an operator, never from CI' }
    @{ Name = 'image-push'
       Pattern = '(?i)docker\s+(push|login)\b|azurecr\.io|sample-registry|\bcontainerapp\b'
       Why = 'an image leaves a runner only through an operator''s deploy.ps1' }
    @{ Name = 'deploy'
       Pattern = '(?i)(?<![\w-])deploy\.ps1'
       Why = 'deploy.ps1 is the local deployment entrypoint, -BuildOnly included' }
)

function Get-Finding {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    # Strip a comment that starts the line or follows whitespace, which is the
    # comment syntax of YAML, bash and PowerShell alike, then join a line that
    # ends in a bash backslash or PowerShell backtick onto the next one while
    # keeping the first line's number for the report.
    $logical = [System.Collections.Generic.List[object]]::new()
    $number = 0
    foreach ($raw in ($Text -split "`r?`n")) {
        $number++
        $stripped = $raw -replace '(^|\s)#.*$', ''
        $last = if ($logical.Count) { $logical[$logical.Count - 1] } else { $null }
        if ($null -ne $last -and $last.Text -match '[\\`]\s*$') {
            $last.Text = ($last.Text -replace '[\\`]\s*$', ' ') + $stripped.TrimStart()
        } else {
            $logical.Add([pscustomobject]@{ Line = $number; Text = $stripped })
        }
    }
    foreach ($entry in $logical) {
        foreach ($rule in $rules) {
            if ($entry.Text -match $rule.Pattern) {
                [pscustomobject]@{
                    Line = $entry.Line
                    Rule = $rule.Name
                    Why  = $rule.Why
                    Text = $entry.Text.Trim()
                }
            }
        }
    }
}

if ($SelfTest) {
    $denied = @(
        "      - uses: azure/login@a1b2c3`n        with:`n          client-id: x"
        "permissions:`n  contents: read`n  id-token: write"
        "        env:`n          AZURE_CLIENT_ID: `${{ secrets.AZURE_CLIENT_ID }}"
        "      run: az login --service-principal"
        "      run: az containerapp update -n sample-demo-app --image x"
        "      run: |`n        az \`n          acr login --name shared"
        "      run: docker push example.azurecr.io/munarium-demo-web:ci"
        "      run: docker login example.azurecr.io"
        "      run: ./deploy.ps1 -Tag v1 -BuildOnly"
        "      run: pwsh -File deploy.ps1 -Tag v1"
        "      run: pwsh -Command Connect-AzAccount -Identity"
    )
    $allowed = @(
        "# deploy.ps1 is never run here; see tools/check_ci_boundary.ps1"
        "      run: docker build --build-arg `"SOURCE_REVISION=`$SOURCE_REVISION`" -t munarium-demo-web:ci ."
        "permissions:`n  contents: read"
        "      run: ./tools/check_ci_boundary.ps1 -SelfTest"
        "      - name: build the demo image # not deploy.ps1"
        "          `"analyzed `$(`$scripts.Count) files, `$(`$results.Count) findings`""
        "        env:`n          GITHUB_TOKEN: `${{ secrets.GITHUB_TOKEN }}"
        "      run: dotnet build Demo.sln -c Release -warnaserror"
    )
    $failures = @()
    foreach ($fixture in $denied) {
        if (-not @(Get-Finding -Text $fixture).Count) { $failures += "not refused: $fixture" }
    }
    foreach ($fixture in $allowed) {
        $hits = @(Get-Finding -Text $fixture)
        if ($hits.Count) { $failures += "wrongly refused ($($hits[0].Rule)): $fixture" }
    }
    if ($failures.Count) {
        $failures | ForEach-Object { Write-Host "FAIL $_" }
        exit 1
    }
    Write-Host "self-test: $($denied.Count) denied and $($allowed.Count) allowed fixtures behave"
    exit 0
}

$root = Split-Path -Parent $PSScriptRoot
$scanDirs = @('.github/workflows', '.github/actions') |
    ForEach-Object { Join-Path $root $_ } |
    Where-Object { Test-Path -LiteralPath $_ }
$files = @(Get-ChildItem -Path $scanDirs -Recurse -File -Include *.yml, *.yaml)
$findings = @(foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName) -replace '\\', '/'
    foreach ($finding in Get-Finding -Text (Get-Content -Raw -LiteralPath $file.FullName)) {
        $finding | Add-Member -NotePropertyName File -NotePropertyValue $relative -PassThru
    }
})
if ($findings.Count) {
    foreach ($finding in $findings) {
        Write-Host "$($finding.File):$($finding.Line): $($finding.Rule) -- $($finding.Why)"
        Write-Host "    $($finding.Text)"
    }
    exit 1
}
Write-Host "checked $($files.Count) workflow files: no step can deploy the demo"
exit 0
