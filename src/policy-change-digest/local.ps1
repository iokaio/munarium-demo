# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([ValidateSet('test','cloud','stop')][string]$Action='test',[string]$Project='digest-wave2')
$ErrorActionPreference='Stop'
if ($Project -notmatch '^digest-[a-z0-9-]+$') { throw 'Use an isolated digest- project name.' }
if ($Action -ne 'stop') {
    $fixtureProfile = if ($env:DEMO_PROFILE) { $env:DEMO_PROFILE } else { 'default' }
    & (Join-Path $PSScriptRoot '../../tools/demo_preflight.ps1') -Demo 'policy-change-digest' -Profile $fixtureProfile
}
Push-Location $PSScriptRoot
try {
    $endpoint=docker context inspect --format '{{.Endpoints.docker.Host}}'
    if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://') { throw 'Select a local Docker context.' }
    if ((docker info --format '{{.OSType}}') -ne 'linux') { throw 'Use Linux containers.' }
    $options=@('compose','--env-file','../../.env.local.sample','-p',$Project,'-f','compose.yaml')
    if ($Action -eq 'cloud') {
        if (-not (Test-Path -LiteralPath '../../.env.local')) { throw 'Configure the three online provider keys in the ignored .env.local file.' }
        $options=@('compose','--env-file','../../.env.local','-p',$Project,'-f','compose.yaml','-f','compose.cloud.yaml')
    }
    function Invoke-Digest([string[]]$Arguments) { & docker @options @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Digest step failed; inspect retained artifacts.' } }
    Invoke-Digest @('config','--quiet')
    if ($Action -eq 'stop') { Invoke-Digest @('stop'); return }
    $run=[guid]::NewGuid().ToString('N'); $report="/work/$Action/$run"; $local="../../artifacts/policy-change-digest/$Action/$run"
    New-Item -ItemType Directory -Force -Path $local | Out-Null
    docker ps --format '{{.Names}} {{.Image}} {{.Status}}' | Set-Content "$local/containers-before.txt"
    git rev-parse HEAD | Set-Content "$local/demo-revision.txt"
    docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' | Set-Content "$local/host.txt"
    "PowerShell local.ps1 -Action $Action -Project $Project; $([Environment]::OSVersion)" | Set-Content "$local/command.txt"
    Invoke-Digest @('build','tests')
    docker image inspect munarium-digest-runner:local --format '{{.Id}} {{.Size}}' | Set-Content "$local/runner.txt"
    Invoke-Digest @('run','--rm','--no-deps','unit','unit','--work',"$report/unit")
    Invoke-Digest @('up','-d','server','provider-fixture','sdk-fixture','faults')
    Invoke-Digest @('run','--rm','--no-deps','generator')
    if ($Action -eq 'cloud') {
        $failed=$false
        foreach($provider in @('openai','anthropic','openrouter')) {
            try {
                Invoke-Digest @('run','--rm','--no-deps','bootstrap','bootstrap','--provider',$provider,'--work',"$report/$provider/bootstrap")
                Invoke-Digest @('run','--rm','--no-deps','tests','usage','--work',"$report/$provider/usage-before")
                Invoke-Digest @('run','--rm','--no-deps','tests','cloud','--work',"$report/$provider")
                Invoke-Digest @('run','--rm','--no-deps','tests','usage','--work',"$report/$provider/usage-after")
            } catch { $failed=$true; Write-Warning "$provider qualification failed; continuing with remaining providers." }
        }
        Write-Output "Reports: artifacts/policy-change-digest/cloud/$run"
        if($failed) { throw 'Online qualification failed.' }
    } else {
        Invoke-Digest @('run','--rm','--no-deps','bootstrap','bootstrap','--work',"$report/bootstrap")
        Invoke-Digest @('run','--rm','--no-deps','tests','controlled','--work',"$report/controlled")
        Invoke-Digest @('restart','server')
        Invoke-Digest @('run','--rm','--no-deps','tests','restarted','--work',"$report/restarted")
        Invoke-Digest @('run','--rm','--no-deps','tests','sdk','--work',"$report/sdk")
        Invoke-Digest @('run','--rm','--no-deps','--entrypoint','/usr/bin/python3','app','/app/support.py','render',"$report/controlled/case-001/digest.md","$report/application.png")
        Write-Output "Reports: artifacts/policy-change-digest/test/$run"
    }
} finally { Pop-Location }
