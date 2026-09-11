# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param(
    [ValidateSet('test', 'cloud', 'stop')][string]$Action = 'test',
    [string]$Project = 'invoice-wave1'
)
$ErrorActionPreference = 'Stop'
if ($Project -notmatch '^invoice-[a-z0-9-]+$') { throw 'Use a project name beginning invoice- with lowercase letters, digits, and hyphens.' }
if ($Action -ne 'stop') {
    $fixtureProfile = if ($env:DEMO_PROFILE) { $env:DEMO_PROFILE } else { 'default' }
    & (Join-Path $PSScriptRoot '../../tools/demo_preflight.ps1') -Demo 'invoice-exception' -Profile $fixtureProfile
}
Push-Location $PSScriptRoot
try {
    $endpoint = docker context inspect --format '{{.Endpoints.docker.Host}}'
    if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://') { throw 'Select a local Docker Desktop/Engine context.' }
    $engine = docker info --format '{{.OSType}}'
    if ($LASTEXITCODE -ne 0 -or $engine -ne 'linux') { throw 'Start Docker with Linux containers.' }
    $composeOptions = @('compose', '--env-file', '../../.env.local.sample', '-p', $Project, '-f', 'compose.yaml')
    if ($Action -eq 'cloud') {
        if (-not (Test-Path -LiteralPath '../../.env.local')) { throw 'Copy .env.local.sample to .env.local and supply all three provider keys and preferred models.' }
        $composeOptions = @('compose', '--env-file', '../../.env.local', '-p', $Project, '-f', 'compose.yaml', '-f', 'compose.cloud.yaml')
    }
    function Invoke-InvoiceCompose {
        param([string[]]$CommandArgs)
        & docker @composeOptions @CommandArgs
        if ($LASTEXITCODE -ne 0) { throw "Invoice Docker step failed (exit $LASTEXITCODE). See artifacts/invoice-exception." }
    }
    Invoke-InvoiceCompose @('config', '--quiet')
    if ($Action -eq 'stop') {
        Invoke-InvoiceCompose @('stop')
        return
    }
    Invoke-InvoiceCompose @('build', 'tests')
    Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'tests', 'unit')
    Invoke-InvoiceCompose @('up', '-d', 'server', 'provider-fixture', 'sdk-fixture')
    Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'generator')
    if ($Action -eq 'cloud') {
        $cloudRun = [guid]::NewGuid().ToString('N')
        $cloudFailed = $false
        foreach ($cloudProvider in @('openai', 'anthropic', 'openrouter')) {
            try {
                Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'bootstrap', 'bootstrap', '--approve', '--provider', $cloudProvider, '--preferred-model')
                try {
                    Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'app', 'process', '--cloud-run', $cloudRun)
                } catch {
                    $cloudFailed = $true
                    Write-Warning "$cloudProvider processing failed; checking its recorded results."
                }
                Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'tests', 'cloud-test', '--cloud-run', $cloudRun)
            } catch {
                $cloudFailed = $true
                Write-Warning "$cloudProvider acceptance failed; continuing with the remaining providers."
            }
        }
        Write-Output "Cloud reports: artifacts/invoice-exception/cloud/$cloudRun"
        if ($cloudFailed) { throw 'One or more cloud providers failed acceptance.' }
        return
    }
    Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'bootstrap')
    if ($Action -eq 'test') {
        Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'tests', 'test')
        Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'tests', 'qualify')
    }
    Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'app', 'process')
    Invoke-InvoiceCompose @('run', '--rm', '--no-deps', 'tests', 'quality')
} finally { Pop-Location }
