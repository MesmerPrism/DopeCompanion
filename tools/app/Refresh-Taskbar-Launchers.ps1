<#
.SYNOPSIS
    Reuses pinned taskbar shortcuts for the DOPE preview and published desktop launch paths.
#>
[CmdletBinding()]
param(
    [string]$TaskbarPinnedPath = (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'),
    [string]$PreviewShortcutName = 'DOPE Companion Preview.lnk',
    [string]$PublishedShortcutName = 'DOPE Companion Published.lnk',
    [string]$DevShortcutName = 'DOPE Companion Dev.lnk'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$previewIconPath = Join-Path $repoRoot 'src\DopeCompanion.App\Assets\Branding\Preview\dope-companion.ico'
$publishedIconPath = Join-Path $repoRoot 'src\DopeCompanion.App\Assets\Branding\Published\dope-companion.ico'
$publishedLauncherHostPath = Join-Path $repoRoot 'tools\app\Start-Desktop-App.vbs'
$devLauncherHostPath = Join-Path $repoRoot 'tools\app\Start-Desktop-App-Local.vbs'
$scriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
$windowsExplorer = Join-Path $env:SystemRoot 'explorer.exe'

if (-not (Test-Path $TaskbarPinnedPath)) {
    throw "Pinned taskbar shortcut folder not found at $TaskbarPinnedPath"
}

if (-not (Test-Path $previewIconPath)) {
    throw "Preview icon not found at $previewIconPath"
}

if (-not (Test-Path $publishedIconPath)) {
    throw "Published icon not found at $publishedIconPath"
}

if (-not (Test-Path $publishedLauncherHostPath)) {
    throw "Published launcher host not found at $publishedLauncherHostPath"
}

if (-not (Test-Path $devLauncherHostPath)) {
    throw "Dev launcher host not found at $devLauncherHostPath"
}

if (-not (Test-Path $scriptHost)) {
    throw "wscript.exe was not found at $scriptHost"
}

if (-not (Test-Path $windowsExplorer)) {
    throw "explorer.exe was not found at $windowsExplorer"
}

$previewPackage = Get-AppxPackage *DopeCompanion* |
    Where-Object { $_.Name -eq 'MesmerPrism.DopeCompanionPreview' } |
    Sort-Object Version -Descending |
    Select-Object -First 1

if ($null -eq $previewPackage) {
    throw 'The DOPE Companion preview package is not installed, so the preview taskbar pin cannot be created.'
}

function Get-CandidateShortcutPaths {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Shell
    )

    Get-ChildItem -LiteralPath $TaskbarPinnedPath -Filter '*.lnk' | ForEach-Object {
        $shortcut = $Shell.CreateShortcut($_.FullName)
        [PSCustomObject]@{
            Path = $_.FullName
            Name = $_.Name
            TargetPath = $shortcut.TargetPath
            Arguments = $shortcut.Arguments
            Description = $shortcut.Description
            IconLocation = $shortcut.IconLocation
        }
    }
}

function Move-ReusableShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Shell,
        [Parameter(Mandatory = $true)]
        [string]$DestinationName,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Preview', 'Published')]
        [string]$ShortcutKind
    )

    $destinationPath = Join-Path $TaskbarPinnedPath $DestinationName
    if (Test-Path $destinationPath) {
        return $destinationPath
    }

    $candidate = Get-CandidateShortcutPaths -Shell $Shell | Where-Object {
        $_.Name -like '*Companion*.lnk' -and
        $_.Name -notlike 'DOPE Companion *.lnk' -and
        (
            ($ShortcutKind -eq 'Preview' -and $_.TargetPath -eq $windowsExplorer -and $_.Arguments -like 'shell:AppsFolder\*!App') -or
            ($ShortcutKind -eq 'Published' -and $_.TargetPath -eq $scriptHost -and $_.Arguments -like '*Start-Desktop-App.vbs*')
        )
    } | Select-Object -First 1

    if ($null -ne $candidate) {
        if (Test-Path $destinationPath) {
            Remove-Item -Force $destinationPath
        }

        Move-Item -LiteralPath $candidate.Path -Destination $destinationPath -Force
    }

    return $destinationPath
}

function Set-Shortcut {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Shell,
        [Parameter(Mandatory = $true)]
        [string]$ShortcutPath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,
        [string]$Arguments,
        [string]$WorkingDirectory,
        [string]$IconLocation,
        [string]$Description
    )

    $shortcut = $Shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $IconLocation
    $shortcut.Description = $Description
    $shortcut.Save()
}

$shell = New-Object -ComObject WScript.Shell
try {
    $previewShortcutPath = Move-ReusableShortcut `
        -Shell $shell `
        -DestinationName $PreviewShortcutName `
        -ShortcutKind Preview

    $publishedShortcutPath = Move-ReusableShortcut `
        -Shell $shell `
        -DestinationName $PublishedShortcutName `
        -ShortcutKind Published

    $devShortcutPath = Join-Path $TaskbarPinnedPath $DevShortcutName

    Set-Shortcut `
        -Shell $shell `
        -ShortcutPath $previewShortcutPath `
        -TargetPath $windowsExplorer `
        -Arguments "shell:AppsFolder\$($previewPackage.PackageFamilyName)!App" `
        -WorkingDirectory $env:SystemRoot `
        -IconLocation "$previewIconPath,0" `
        -Description 'Launch the installed DOPE Companion preview package'

    Set-Shortcut `
        -Shell $shell `
        -ShortcutPath $publishedShortcutPath `
        -TargetPath $scriptHost `
        -Arguments "//B //nologo `"$publishedLauncherHostPath`" -SkipInstalledPackage -Refresh" `
        -WorkingDirectory $repoRoot `
        -IconLocation "$publishedIconPath,0" `
        -Description 'Launch the published local DOPE Companion desktop build'

    Set-Shortcut `
        -Shell $shell `
        -ShortcutPath $devShortcutPath `
        -TargetPath $scriptHost `
        -Arguments "//B //nologo `"$devLauncherHostPath`" -Configuration Release -NoBuild" `
        -WorkingDirectory $repoRoot `
        -IconLocation "$publishedIconPath,0" `
        -Description 'Launch the repo-local DOPE Companion development build'

    Get-CandidateShortcutPaths -Shell $shell | Where-Object {
        $_.Name -like '*Companion*.lnk' -and
        $_.Name -notin @($PreviewShortcutName, $PublishedShortcutName, $DevShortcutName) -and
        (
            ($_.TargetPath -eq $windowsExplorer -and $_.Arguments -like 'shell:AppsFolder\*!App') -or
            ($_.TargetPath -eq $scriptHost -and ($_.Arguments -like '*Start-Desktop-App.vbs*' -or $_.Arguments -like '*Start-Desktop-App-Local.vbs*'))
        )
    } | ForEach-Object {
        if (Test-Path $_.Path) {
            Remove-Item -LiteralPath $_.Path -Force
        }
    }
}
finally {
    if ($null -ne $shell -and [System.Runtime.InteropServices.Marshal]::IsComObject($shell)) {
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }
}

[PSCustomObject]@{
    PreviewShortcut = $previewShortcutPath
    PublishedShortcut = $publishedShortcutPath
    DevShortcut = $devShortcutPath
    PreviewPackageFamily = $previewPackage.PackageFamilyName
}
