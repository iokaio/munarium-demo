# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([ValidateSet('test','cloud','stop')][string]$Action = 'test', [string]$Project = 'quality-wave2')
$ErrorActionPreference = 'Stop'
if ($Project -notmatch '^quality-[a-z0-9-]+$') { throw 'Use an isolated quality- project name.' }
if ($Action -ne 'stop') {
    $fixtureProfile = if ($env:DEMO_PROFILE) { $env:DEMO_PROFILE } else { 'default' }
    & (Join-Path $PSScriptRoot '../../tools/demo_preflight.ps1') -Demo 'quality-investigation' -Profile $fixtureProfile
}
Push-Location $PSScriptRoot
try {
    $endpoint = docker context inspect --format '{{.Endpoints.docker.Host}}'
    if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://') { throw 'Select a local Docker context.' }
    $engine = docker info --format '{{.OSType}}'
    if ($LASTEXITCODE -ne 0 -or $engine -ne 'linux') { throw 'Start Docker with Linux containers.' }
    $options = @('compose','--env-file','../../.env.local.sample','-p',$Project,'-f','compose.yaml')
    if ($Action -eq 'cloud') {
        if (-not (Test-Path -LiteralPath '../../.env.local')) { throw 'Supply the three online provider keys in the ignored .env.local file.' }
        $options = @('compose','--env-file','../../.env.local','-p',$Project,'-f','compose.yaml','-f','compose.cloud.yaml')
    }
    function Invoke-QualityCompose([string[]]$CommandArgs) {
        & docker @options @CommandArgs
        if ($LASTEXITCODE -ne 0) { throw "Quality demo step failed (exit $LASTEXITCODE). Inspect artifacts/quality-investigation." }
    }
    Invoke-QualityCompose @('config','--quiet')
    if ($Action -eq 'stop') { Invoke-QualityCompose @('stop'); return }
    $run = [guid]::NewGuid().ToString('N')
    $report = "/work/$Action/$run"
    $localReport = "../../artifacts/quality-investigation/$Action/$run"
    New-Item -ItemType Directory -Force -Path $localReport | Out-Null
    docker ps --format '{{.Names}} {{.Image}} {{.Status}}' | Set-Content "$localReport/containers-before.txt"
    docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' | Set-Content "$localReport/docker-host.txt"
    git rev-parse HEAD | Set-Content "$localReport/demo-revision.txt"
    "PowerShell local.ps1 -Action $Action -Project $Project; host=$([Environment]::OSVersion)" | Set-Content "$localReport/command.txt"
    Invoke-QualityCompose @('build','tests')
    docker image inspect munarium-quality-runner:local --format '{{.Id}} {{.Size}}' | Set-Content "$localReport/runner-image.txt"
    Invoke-QualityCompose @('run','--rm','--no-deps','-e',"QUALITY_REPORT_DIR=$report/unit",'unit','unit')
    Invoke-QualityCompose @('up','-d','server','provider-fixture','faults')
    Invoke-QualityCompose @('run','--rm','--no-deps','generator')
    if ($Action -eq 'cloud') {
        $failed = $false
        foreach ($provider in @('openai','anthropic','openrouter')) {
            try {
                Invoke-QualityCompose @('run','--rm','--no-deps','bootstrap','bootstrap',$provider,'--approve')
                Invoke-QualityCompose @('run','--rm','--no-deps','-e',"QUALITY_REPORT_DIR=$report/$provider",'tests',"cloud-$provider")
            } catch { $failed = $true; Write-Warning "$provider qualification failed; continuing with remaining providers." }
        }
        Write-Output "Reports: artifacts/quality-investigation/cloud/$run"
        if ($failed) { throw 'One or more online providers failed qualification.' }
    } else {
        Invoke-QualityCompose @('run','--rm','--no-deps','bootstrap')
        Invoke-QualityCompose @('run','--rm','--no-deps','-e',"QUALITY_REPORT_DIR=$report/controlled",'tests','controlled')
        Invoke-QualityCompose @('restart','server')
        Invoke-QualityCompose @('run','--rm','--no-deps','-e',"QUALITY_REPORT_DIR=$report/restarted",'tests','restarted')
        Invoke-QualityCompose @('run','--rm','--no-deps','-e',"QUALITY_REPORT_DIR=$report/sdk",'tests','qualify')
        Invoke-QualityCompose @('run','--rm','--no-deps','app','render',"$report/controlled/case-001/packet.md","$report/application.png")
        Write-Output "Reports: artifacts/quality-investigation/test/$run"
    }
} finally { Pop-Location }
