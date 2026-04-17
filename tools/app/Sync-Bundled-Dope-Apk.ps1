<#
.SYNOPSIS
    Refreshes the bundled DOPE projected-feed Colorama APK mirror and updates its pinned compatibility hash.
#>
[CmdletBinding()]
param(
    [string]$SourceApkPath,
    [string]$VersionName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hashBytes)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$defaultDopeRepoRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '..\Dynamic Oscillatory Pattern Entrainment'))
$resolvedSourceApkPath = if ([string]::IsNullOrWhiteSpace($SourceApkPath)) {
    $defaultCandidates = @(
        [System.IO.Path]::GetFullPath((Join-Path $defaultDopeRepoRoot 'Builds\Quest\DynamicOscillatoryPatternEntrainment-SynedelicaPassthroughOverlayMultiLayer.apk')),
        [System.IO.Path]::GetFullPath((Join-Path $defaultDopeRepoRoot 'Builds\Quest\DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk'))
    )

    $defaultCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
} else {
    [System.IO.Path]::GetFullPath($SourceApkPath)
}

if (-not (Test-Path $resolvedSourceApkPath)) {
    throw "Source DOPE APK not found at $resolvedSourceApkPath"
}

$bundledApkPath = Join-Path $repoRoot 'samples\quest-session-kit\APKs\DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk'
$compatibilityPath = Join-Path $repoRoot 'samples\quest-session-kit\APKs\compatibility.json'

Copy-Item -LiteralPath $resolvedSourceApkPath -Destination $bundledApkPath -Force
$sha256 = Get-Sha256Hex -Path $bundledApkPath

$compatibility = Get-Content -LiteralPath $compatibilityPath -Raw | ConvertFrom-Json
if ($null -eq $compatibility.apps -or @($compatibility.apps).Count -lt 1) {
    throw "No compatibility app entries were found in $compatibilityPath"
}

$compatibility.apps[0].sha256 = $sha256
if (-not [string]::IsNullOrWhiteSpace($VersionName)) {
    $compatibility.apps[0] | Add-Member -NotePropertyName versionName -NotePropertyValue $VersionName -Force
}

$compatibility | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $compatibilityPath -Encoding utf8

Write-Host "Bundled DOPE APK refreshed from $resolvedSourceApkPath" -ForegroundColor Green
Write-Host "Updated SHA256 to $sha256" -ForegroundColor Green
