# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([ValidateSet('test','cloud','stop')][string]$Action='test',[string]$Project='bench-wave2')
$ErrorActionPreference='Stop'
if ($Project -notmatch '^bench-[a-z0-9-]+$') { throw 'Use an isolated bench- project name.' }
if ($Action -ne 'stop') {
    $fixtureProfile = if ($env:DEMO_PROFILE) { $env:DEMO_PROFILE } else { 'default' }
    & (Join-Path $PSScriptRoot '../../tools/demo_preflight.ps1') -Demo 'retrieval-evaluation' -Profile $fixtureProfile
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
    function Invoke-Bench([string[]]$Arguments) { & docker @options @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Bench step failed; inspect retained artifacts.' } }
    Invoke-Bench @('config','--quiet')
    if ($Action -eq 'stop') { Invoke-Bench @('stop'); return }
    $run=[guid]::NewGuid().ToString('N'); $report="/work/$Action/$run"; $local="../../artifacts/retrieval-evaluation/$Action/$run"
    New-Item -ItemType Directory -Force -Path $local | Out-Null
    docker ps --format '{{.Names}} {{.Image}} {{.Status}}' | Set-Content "$local/containers-before.txt"
    git rev-parse HEAD | Set-Content "$local/demo-revision.txt"
    docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' | Set-Content "$local/host.txt"
    "PowerShell local.ps1 -Action $Action -Project $Project; $([Environment]::OSVersion)" | Set-Content "$local/command.txt"
    Invoke-Bench @('build','tests')
    docker image inspect munarium-bench-runner:local --format '{{.Id}} {{.Size}}' | Set-Content "$local/runner.txt"
    Invoke-Bench @('run','--rm','--no-deps','unit','unit','--work',"$report/unit")
    Invoke-Bench @('up','-d','server','provider-fixture','sdk-fixture','faults')
    Invoke-Bench @('run','--rm','--no-deps','generator')
    if ($Action -eq 'cloud') {
        $failed=$false
        foreach($provider in @('openai','anthropic','openrouter')) {
            try {
                Invoke-Bench @('run','--rm','--no-deps','bootstrap','bootstrap','--provider',$provider,'--work',"$report/$provider/bootstrap")
                Invoke-Bench @('run','--rm','--no-deps','tests','usage','--work',"$report/$provider/usage-before")
                Invoke-Bench @('run','--rm','--no-deps','tests','cloud','--work',"$report/$provider")
                Invoke-Bench @('run','--rm','--no-deps','tests','usage','--work',"$report/$provider/usage-after")
            } catch { $failed=$true; Write-Warning "$provider qualification failed; continuing with remaining providers." }
        }
        Write-Output "Reports: artifacts/retrieval-evaluation/cloud/$run"
        if($failed) { throw 'Online qualification failed.' }
    } else {
        Invoke-Bench @('run','--rm','--no-deps','bootstrap','bootstrap','--work',"$report/bootstrap")
        Invoke-Bench @('run','--rm','--no-deps','tests','controlled','--work',"$report/controlled")
        Invoke-Bench @('restart','server')
        Invoke-Bench @('run','--rm','--no-deps','tests','restarted','--work',"$report/restarted")
        Invoke-Bench @('run','--rm','--no-deps','tests','sdk','--work',"$report/sdk")
        Invoke-Bench @('run','--rm','--no-deps','scorer','score','--work',"$report/controlled/experiments")
        Invoke-Bench @('run','--rm','--no-deps','--entrypoint','/usr/bin/python3','scorer','/app/support.py','plot',"$report/controlled/experiments/metrics.json","$report/application.png")
        Write-Output "Reports: artifacts/retrieval-evaluation/test/$run"
    }
} finally { Pop-Location }
