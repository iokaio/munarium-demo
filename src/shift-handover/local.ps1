# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([ValidateSet('test','cloud','stop')][string]$Action='test',[string]$Project='shift-wave2')
$ErrorActionPreference='Stop'
if ($Project -notmatch '^shift-[a-z0-9-]+$') { throw 'Use an isolated shift- project name.' }
if ($Action -ne 'stop') {
    $fixtureProfile = if ($env:DEMO_PROFILE) { $env:DEMO_PROFILE } else { 'default' }
    & (Join-Path $PSScriptRoot '../../tools/demo_preflight.ps1') -Demo 'shift-handover' -Profile $fixtureProfile
}
Push-Location $PSScriptRoot
try {
    $endpoint=docker context inspect --format '{{.Endpoints.docker.Host}}'
    if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://') { throw 'Select a local Docker context.' }
    if ((docker info --format '{{.OSType}}') -ne 'linux') { throw 'Use Linux containers.' }
    function Invoke-Shift([string[]]$Arguments) { & docker compose --env-file ../../.env.local.sample -p $Project -f compose.yaml @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Shift journal step failed; inspect retained artifacts.' } }
    Invoke-Shift @('config','--quiet')
    if ($Action -eq 'stop') { Invoke-Shift @('stop'); return }
    if ($Action -eq 'cloud') { Write-Output 'Not applicable: shift journal has no model calls. Use test for the complete suite.'; return }
    $run=[guid]::NewGuid().ToString('N'); $report="/work/test/$run"; $local="../../artifacts/shift-handover/test/$run"
    New-Item -ItemType Directory -Force -Path $local | Out-Null
    docker ps --format '{{.Names}} {{.Image}} {{.Status}}' | Set-Content "$local/containers-before.txt"
    git rev-parse HEAD | Set-Content "$local/demo-revision.txt"
    docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' | Set-Content "$local/host.txt"
    "PowerShell local.ps1 -Action $Action -Project $Project; $([Environment]::OSVersion)" | Set-Content "$local/command.txt"
    Invoke-Shift @('build','tests')
    docker image inspect munarium-shift-runner:local --format '{{.Id}} {{.Size}}' | Set-Content "$local/runner.txt"
    Invoke-Shift @('run','--rm','--no-deps','-e',"SHIFT_REPORT_DIR=$report/unit",'unit','unit')
    Invoke-Shift @('up','-d','server','faults')
    Invoke-Shift @('run','--rm','--no-deps','generator')
    Invoke-Shift @('run','--rm','--no-deps','-e',"SHIFT_RUN_ID=$run",'bootstrap')
    Invoke-Shift @('run','--rm','--no-deps','-e',"SHIFT_REPORT_DIR=$report/controlled",'tests','controlled')
    $env:SHIFT_RACE_WORK="$report/race"
    Invoke-Shift @('run','--rm','--no-deps','operator','race','init',"$report/race")
    Invoke-Shift @('up','--no-deps','--abort-on-container-failure','race-a','race-b')
    Invoke-Shift @('run','--rm','--no-deps','operator','race','verify',"$report/race")
    Invoke-Shift @('restart','server')
    Invoke-Shift @('run','--rm','--no-deps','-e',"SHIFT_REPORT_DIR=$report/controlled",'tests','restarted')
    Invoke-Shift @('run','--rm','--no-deps','-e',"SHIFT_REPORT_DIR=$report/sdk",'tests','qualify')
    Invoke-Shift @('run','--rm','--no-deps','--entrypoint','python3','app','/app/support.py','render',"$report/controlled/shift-001/historical-shift-001.txt","$report/application.png")
    Write-Output "Reports: artifacts/shift-handover/test/$run"
} finally { Pop-Location }
