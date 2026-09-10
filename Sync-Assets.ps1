# SPDX-License-Identifier: Apache-2.0
#Requires -Version 7
[CmdletBinding()]
param([switch]$Check)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $PSScriptRoot 'vendor/assets.json') -Raw | ConvertFrom-Json
foreach ($asset in $manifest.files) {
    $source = Join-Path $PSScriptRoot $asset.path
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.sha256) {
        throw "Asset checksum mismatch: $($asset.path)"
    }
}
if (-not $Check) {
    $destination = Join-Path $PSScriptRoot 'src/Demo.Web/wwwroot/downloads/runbooks'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem (Join-Path $PSScriptRoot 'vendor/runbooks') -Filter '*.yaml' | Copy-Item -Destination $destination -Force
}
Write-Host 'Verified standalone demo assets.'
