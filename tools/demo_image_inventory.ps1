# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([string]$Output='artifacts/demo-qualification/image-inventory.json')
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$outputPath=[IO.Path]::GetFullPath((Join-Path $repoPath $Output))
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $repoPath 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Write the inventory under repository artifacts' }
$references=@(Get-ChildItem (Join-Path $repoPath 'src/*/Dockerfile'),(Join-Path $repoPath 'src/*/compose.yaml') | ForEach-Object {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($_.FullName),'(?m)^\s*(?:FROM|image:)\s+([^\s]+@sha256:[a-f0-9]{64})')) { $match.Groups[1].Value }
} | Sort-Object -Unique)
$records=@(foreach ($reference in $references) {
    $raw=& docker buildx imagetools inspect $reference --raw
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect pinned manifest: $reference" }
    $manifest=$raw | ConvertFrom-Json
    if ($manifest.manifests) {
        $variants=@(foreach ($entry in $manifest.manifests | Where-Object { $_.platform.os -eq 'linux' -and $_.platform.architecture -in @('amd64','arm64') }) {
            $childReference=($reference -split '@')[0]+'@'+$entry.digest
            $childRaw=& docker buildx imagetools inspect $childReference --raw
            if ($LASTEXITCODE -ne 0) { throw "Cannot inspect child manifest: $childReference" }
            $child=$childRaw | ConvertFrom-Json
            @{architecture=$entry.platform.architecture;digest=$entry.digest;compressed_layer_bytes=($child.layers | Measure-Object -Property size -Sum).Sum}
        })
    } else {
        $arch=& docker image inspect $reference --format '{{.Architecture}}'
        if ($LASTEXITCODE -ne 0) { throw "Single-manifest architecture requires the previously built local image: $reference" }
        $variants=@(@{architecture=$arch;digest=($reference -split '@')[1];compressed_layer_bytes=($manifest.layers | Measure-Object -Property size -Sum).Sum})
    }
    @{reference=$reference;linux_variants=$variants}
})
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath -Parent) | Out-Null
@{utc=[DateTimeOffset]::UtcNow.ToString('o');images=$records;measurement='Registry compressed layer bytes, before cache reuse; excludes package-manager, compiler and source-checkout downloads'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Output "Recorded $($records.Count) pinned image manifests in $Output"
