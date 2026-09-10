# SPDX-License-Identifier: Apache-2.0
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$AppName,
    [string]$ResourceGroup,
    [string]$Registry,
    [hashtable]$Environment = @{},
    [switch]$BuildOnly
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Sync-Assets.ps1') -Check
if (-not $BuildOnly -and (-not $AppName -or -not $ResourceGroup -or -not $Registry)) {
    throw 'Supply AppName, ResourceGroup, and Registry, or use BuildOnly.'
}
$registryHost = '{0}.{1}' -f $Registry, 'azurecr.io'
$image = if ($BuildOnly) { "munarium-demo-web:$Tag" } else { "$registryHost/munarium-demo-web:$Tag" }
docker build -t $image $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'Image build failed' }
if ($BuildOnly) { return }
az acr login --name $Registry
if ($LASTEXITCODE -ne 0) { throw 'Registry login failed' }
docker push $image
if ($LASTEXITCODE -ne 0) { throw 'Image push failed' }
$digest = az acr repository show -n $Registry --image "munarium-demo-web:$Tag" --query digest -o tsv
if ($LASTEXITCODE -ne 0 -or $digest -notmatch '^sha256:[a-f0-9]{64}$') { throw 'Cannot resolve image digest' }
$image = "$registryHost/munarium-demo-web@$digest"
$updateArgs = @('containerapp', 'update', '-n', $AppName, '-g', $ResourceGroup,
    '--image', $image, '--query', 'properties.latestRevisionName', '-o', 'tsv')
if ($Environment.Count) {
    $updateArgs += '--set-env-vars'
    foreach ($entry in $Environment.GetEnumerator()) {
        if ($entry.Key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw 'Invalid environment variable name' }
        $updateArgs += "$($entry.Key)=$($entry.Value)"
    }
}
$revision = az @updateArgs
if ($LASTEXITCODE -ne 0) { throw 'Revision update failed' }
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    $actual = az containerapp revision show -n $AppName -g $ResourceGroup --revision $revision -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect revision' }
    if ($actual.properties.template.containers[0].image -ne $image) { throw 'Image identity mismatch' }
    if ($actual.properties.healthState -eq 'Healthy') { break }
    Start-Sleep -Seconds 5
}
if ($actual.properties.healthState -ne 'Healthy') { throw 'Revision did not become healthy' }
$app = az containerapp show -n $AppName -g $ResourceGroup -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $app.properties.latestReadyRevisionName -ne $revision) { throw 'New revision is not ready' }
$ready = Invoke-RestMethod "https://$($app.properties.configuration.ingress.fqdn)/readyz"
if (-not $ready.ok -or -not $ready.munarium) { throw 'Backend readiness failed' }
Write-Host 'Verified deployment image and readiness.'
