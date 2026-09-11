# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param(
    [ValidateSet('test', 'cloud', 'desktop', 'publish', 'stop')][string]$Action = 'test',
    [string]$Project = 'policy-wave1',
    [ValidateSet('fixture', 'openai', 'anthropic', 'openrouter')][string]$Provider = 'fixture',
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
if ($Project -notmatch '^policy-[a-z0-9-]+$') { throw 'Use a dedicated policy- project name with lowercase letters, digits, and hyphens.' }
Push-Location $PSScriptRoot
try {
    $endpoint = docker context inspect --format '{{.Endpoints.docker.Host}}'
    if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://') { throw 'Select a local Docker context.' }
    $engine = docker info --format '{{.OSType}}'
    if ($LASTEXITCODE -ne 0 -or $engine -ne 'linux') { throw 'Start Docker with Linux containers.' }
    $composeOptions = @('compose', '--env-file', '../../.env.local.sample', '-p', $Project, '-f', 'compose.yaml')
    if ($Action -eq 'cloud' -or ($Action -eq 'desktop' -and $Provider -in @('openai', 'anthropic', 'openrouter'))) {
        if (-not (Test-Path -LiteralPath '../../.env.local')) { throw 'Supply provider keys in root .env.local using .env.local.sample.' }
        $composeOptions = @('compose', '--env-file', '../../.env.local', '-p', $Project, '-f', 'compose.yaml', '-f', 'compose.cloud.yaml')
    }
    if ($Action -eq 'desktop') { $composeOptions += @('-f', 'compose.desktop.yaml') }
    function Invoke-PolicyCompose {
        param([string[]]$CommandArgs)
        & docker @composeOptions @CommandArgs
        if ($LASTEXITCODE -ne 0) { throw "Policy Docker step failed (exit $LASTEXITCODE); inspect artifacts/employee-policy-assistant." }
    }
    Invoke-PolicyCompose @('config', '--quiet')
    if ($Action -eq 'stop') { Invoke-PolicyCompose @('stop'); return }
    Invoke-PolicyCompose @('build', 'tests')
    if ($Action -eq 'publish') {
        Invoke-PolicyCompose @('run', '--rm', '--no-deps', '--entrypoint', 'dotnet', 'tests', 'publish', 'Policy.Desktop/Policy.Desktop.csproj', '-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-o', "/work/desktop/$Runtime")
        return
    }
    $run = [guid]::NewGuid().ToString('N')
    Invoke-PolicyCompose @('run', '--rm', '--no-deps', '-e', "POLICY_REPORT_DIR=/work/controlled/$run", 'tests', 'unit')
    Invoke-PolicyCompose @('up', '-d', 'server', 'provider-fixture')
    Invoke-PolicyCompose @('run', '--rm', '--no-deps', 'generator')
    if ($Action -eq 'cloud') {
        $failed = $false
        foreach ($provider in @('openai', 'anthropic', 'openrouter')) {
            try {
                Invoke-PolicyCompose @('run', '--rm', '--no-deps', 'bootstrap', 'bootstrap', $provider, '--approve')
                Invoke-PolicyCompose @('run', '--rm', '--no-deps', '-e', "POLICY_REPORT_DIR=/work/cloud/$run/$provider", 'tests', 'cloud', $provider)
            } catch { $failed = $true; Write-Warning "$provider acceptance failed; continuing with remaining providers." }
        }
        Write-Output "Reports: artifacts/employee-policy-assistant/cloud/$run"
        if ($failed) { throw 'One or more cloud providers failed.' }
        return
    }
    $selectedProvider = if ($Action -eq 'desktop') { $Provider } else { 'fixture' }
    Invoke-PolicyCompose @('run', '--rm', '--no-deps', 'bootstrap', 'bootstrap', $selectedProvider, '--approve')
    if ($Action -eq 'desktop') {
        Invoke-PolicyCompose @('run', '--rm', '--no-deps', '--entrypoint', 'sh', 'bootstrap', '-c', 'mkdir -p /work/desktop/credentials && cp /credentials/*.json /work/desktop/credentials/')
        Write-Output 'Desktop Server: http://127.0.0.1:18082; issued tutorial grants: artifacts/employee-policy-assistant/desktop/credentials'
        return
    }
    Invoke-PolicyCompose @('run', '--rm', '--no-deps', '-e', "POLICY_REPORT_DIR=/work/$Action/$run", 'tests', 'test')
    if ($Action -eq 'test') { Invoke-PolicyCompose @('run', '--rm', '--no-deps', '-e', "POLICY_REPORT_DIR=/work/test/$run/sdk", 'tests', 'qualify') }
    Write-Output "Reports: artifacts/employee-policy-assistant/$Action/$run"
} finally { Pop-Location }
