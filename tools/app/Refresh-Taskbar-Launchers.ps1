<#
.SYNOPSIS
    Keeps one packaged DOPE Companion taskbar pin plus one repo-local DOPE Companion Dev pin.
#>
[CmdletBinding()]
param(
    [string]$TaskbarPinnedPath = (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'),
    [string]$CompanionShortcutName = 'DOPE Companion.lnk',
    [string]$DevShortcutName = 'DOPE Companion Dev.lnk'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$devIconPath = Join-Path $repoRoot 'src\DopeCompanion.App\Assets\dope-companion-dev.ico'
$devLauncherHostPath = Join-Path $repoRoot 'tools\app\Start-Desktop-App-Local.vbs'
$repoLocalBuildRoot = Join-Path $repoRoot 'src\DopeCompanion.App\bin'
$scriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
$windowsExplorer = Join-Path $env:SystemRoot 'explorer.exe'
$obsoleteShortcutNames = @(
    'DOPE Companion Preview.lnk',
    'DOPE Companion Published.lnk'
)

if (-not (Test-Path $TaskbarPinnedPath)) {
    throw "Pinned taskbar shortcut folder not found at $TaskbarPinnedPath"
}

if (-not (Test-Path $devIconPath)) {
    throw "Dev icon not found at $devIconPath"
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

$packagedApp = Get-AppxPackage *DopeCompanion* |
    Where-Object { $_.Name -eq 'MesmerPrism.DopeCompanionPreview' } |
    Sort-Object Version -Descending |
    Select-Object -First 1

if ($null -eq $packagedApp) {
    throw 'The DOPE Companion package is not installed, so the packaged taskbar pin cannot be created.'
}

if (-not [string]::IsNullOrWhiteSpace($packagedApp.InstallLocation)) {
    $packagedExecutablePath = Join-Path $packagedApp.InstallLocation 'DopeCompanion.App\DopeCompanion.exe'
    if (-not (Test-Path $packagedExecutablePath)) {
        $packagedExecutablePath = Join-Path $packagedApp.InstallLocation 'DopeCompanion.exe'
    }

    $packagedIconPath = if (Test-Path $packagedExecutablePath) {
        $packagedExecutablePath
    }
    else {
        Join-Path $repoRoot 'src\DopeCompanion.App\Assets\dope-companion.ico'
    }
}
else {
    $packagedIconPath = Join-Path $repoRoot 'src\DopeCompanion.App\Assets\dope-companion.ico'
}

function Get-ShortcutMetadata {
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
        }
    }
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
    $companionShortcutPath = Join-Path $TaskbarPinnedPath $CompanionShortcutName
    $devShortcutPath = Join-Path $TaskbarPinnedPath $DevShortcutName

    Set-Shortcut `
        -Shell $shell `
        -ShortcutPath $companionShortcutPath `
        -TargetPath $windowsExplorer `
        -Arguments "shell:AppsFolder\$($packagedApp.PackageFamilyName)!App" `
        -WorkingDirectory $env:SystemRoot `
        -IconLocation "$packagedIconPath,0" `
        -Description 'Launch the installed DOPE Companion app'

    Set-Shortcut `
        -Shell $shell `
        -ShortcutPath $devShortcutPath `
        -TargetPath $scriptHost `
        -Arguments "//B //nologo `"$devLauncherHostPath`"" `
        -WorkingDirectory $repoRoot `
        -IconLocation "$devIconPath,0" `
        -Description 'Launch the repo-local DOPE Companion development build'

    foreach ($obsoleteName in $obsoleteShortcutNames) {
        $obsoletePath = Join-Path $TaskbarPinnedPath $obsoleteName
        if (Test-Path $obsoletePath) {
            Remove-Item -LiteralPath $obsoletePath -Force
        }
    }

    Get-ShortcutMetadata -Shell $shell | Where-Object {
        $_.Name -like '*Companion*.lnk' -and
        $_.Name -notin @($CompanionShortcutName, $DevShortcutName) -and
        (
            ($_.TargetPath -eq $windowsExplorer -and $_.Arguments -like 'shell:AppsFolder\*!App') -or
            ($_.TargetPath -eq $scriptHost -and $_.Arguments -like '*Start-Desktop-App-Local.vbs*') -or
            ($_.TargetPath -like "$repoLocalBuildRoot*" -and $_.TargetPath -like '*\DopeCompanion.exe')
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
    CompanionShortcut = $companionShortcutPath
    DevShortcut = $devShortcutPath
    PackagedAppFamily = $packagedApp.PackageFamilyName
}
