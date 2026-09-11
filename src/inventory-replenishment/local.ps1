# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([ValidateSet('test','cloud','stop')][string]$Action='test',[string]$Project='inventory-wave3')
$ErrorActionPreference='Stop'
if ($Project -notmatch '^inventory-[a-z0-9-]+$') { throw 'Use an isolated inventory- project name.' }
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
    function Invoke-Inventory([string[]]$Arguments) { & docker @options @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Inventory step failed; inspect retained artifacts.' } }
    Invoke-Inventory @('config','--quiet')
    if ($Action -eq 'stop') { Invoke-Inventory @('stop'); return }
    $run=[guid]::NewGuid().ToString('N'); $report="/work/$Action/$run"; $local="../../artifacts/inventory-replenishment/$Action/$run"
    New-Item -ItemType Directory -Force -Path $local | Out-Null
    docker ps --format '{{.Names}} {{.Image}} {{.Status}}' | Set-Content "$local/containers-before.txt"
    git rev-parse HEAD | Set-Content "$local/demo-revision.txt"
    docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' | Set-Content "$local/host.txt"
    "PowerShell local.ps1 -Action $Action -Project $Project; $([Environment]::OSVersion)" | Set-Content "$local/command.txt"
    docker build -t munarium-inventory-matrix:local 'https://github.com/iokaio/munarium.git#bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3:matrix'
    if ($LASTEXITCODE -ne 0) { throw 'Pinned Matrix build failed.' }
    docker image inspect munarium-inventory-matrix:local --format '{{.Id}} {{.Size}}' | Set-Content "$local/matrix-image.txt"
    Invoke-Inventory @('build','tests')
    docker image inspect munarium-inventory-runner:local --format '{{.Id}} {{.Size}}' | Set-Content "$local/runner.txt"
    Invoke-Inventory @('run','--rm','--no-deps','unit','unit','--work',"$report/unit")
    Invoke-Inventory @('up','-d','server','matrix','provider-fixture','sdk-fixture','faults')
    Invoke-Inventory @('run','--rm','--no-deps','generator')
    Invoke-Inventory @('run','--rm','--no-deps','seed')
    if ($Action -eq 'cloud') {
        $failed=$false
        foreach($provider in @('openai','anthropic','openrouter')) {
            try {
                Invoke-Inventory @('run','--rm','--no-deps','bootstrap','bootstrap','--provider',$provider,'--work',"$report/$provider/bootstrap")
                Invoke-Inventory @('run','--rm','--no-deps','tests','usage','--work',"$report/$provider/usage-before")
                Invoke-Inventory @('run','--rm','--no-deps','tests','cloud','--work',"$report/$provider")
                Invoke-Inventory @('run','--rm','--no-deps','tests','usage','--work',"$report/$provider/usage-after")
            } catch { $failed=$true; Write-Warning "$provider qualification failed; continuing with remaining providers." }
        }
        Write-Output "Reports: artifacts/inventory-replenishment/cloud/$run"
        if($failed) { throw 'Online qualification failed.' }
    } else {
        Invoke-Inventory @('run','--rm','--no-deps','bootstrap','bootstrap','--work',"$report/bootstrap")
        Invoke-Inventory @('run','--rm','--no-deps','tests','controlled','--work',"$report/controlled")
        Invoke-Inventory @('stop','inventory-db')
        try { Invoke-Inventory @('run','--rm','--no-deps','tests','sourceoutage','--work',"$report/sourceoutage") } finally { Invoke-Inventory @('start','inventory-db') }
        Invoke-Inventory @('restart','server')
        Invoke-Inventory @('run','--rm','--no-deps','server-ready')
        Invoke-Inventory @('restart','matrix')
        Invoke-Inventory @('run','--rm','--no-deps','tests','restarted','--work',"$report/restarted")
        Invoke-Inventory @('run','--rm','--no-deps','tests','sdk','--work',"$report/sdk")
        Invoke-Inventory @('run','--rm','--no-deps','tests','matrixsdk','--work',"$report/matrixsdk")
        Invoke-Inventory @('run','--rm','--no-deps','--entrypoint','/usr/bin/python3','app','/app/support.py','render',"$report/controlled/case-001/inventory.md","$report/application.png")
        Write-Output "Reports: artifacts/inventory-replenishment/test/$run"
    }
} finally { Pop-Location }
