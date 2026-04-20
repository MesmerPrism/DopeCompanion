<#
.SYNOPSIS
    Launches the local repo build of the companion app for development.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$RefreshLauncher,
    [switch]$Wait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repoRoot 'src\DopeCompanion.App\DopeCompanion.App.csproj'
$refreshLauncherScript = Join-Path $PSScriptRoot 'Refresh-Local-Desktop-Launcher.ps1'
[xml]$projectXml = Get-Content -Path $projectPath
$targetFramework = @($projectXml.Project.PropertyGroup.TargetFramework | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })[0]
$outputPath = Join-Path $repoRoot "src\DopeCompanion.App\bin\$Configuration\$targetFramework"
$exePath = Join-Path $outputPath 'DopeCompanion.exe'

if (-not (Test-Path $projectPath)) {
    throw "App project not found at $projectPath"
}

if ($RefreshLauncher) {
    if (-not (Test-Path $refreshLauncherScript)) {
        throw "Local launcher refresh script not found at $refreshLauncherScript"
    }

    & $refreshLauncherScript | Out-Null
}

if (-not $NoBuild) {
    $dotnet = Get-Command dotnet -ErrorAction Stop
    $buildArguments = @(
        'build',
        $projectPath,
        '-c', $Configuration,
        '-p:DopeCompanionBrand=Dev'
    )

    $buildProcess = Start-Process `
        -FilePath $dotnet.Source `
        -ArgumentList $buildArguments `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -PassThru `
        -Wait

    if ($buildProcess.ExitCode -ne 0) {
        throw "Local dev build failed with exit code $($buildProcess.ExitCode)."
    }
}
elseif (-not (Test-Path $exePath)) {
    throw "Local dev executable not found at $exePath. Re-run without -NoBuild first."
}

$existingProcess = Get-Process -Name 'DopeCompanion' -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            $_.Path -and [string]::Equals($_.Path, $exePath, [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    } |
    Select-Object -First 1

if ($null -ne $existingProcess) {
    if ($Wait) {
        Wait-Process -Id $existingProcess.Id
    }

    return [PSCustomObject]@{
        LaunchMode = 'LocalDevExe'
        ReusedProcess = $true
        ProcessId = $existingProcess.Id
        ExecutablePath = $exePath
    }
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $exePath
$startInfo.WorkingDirectory = $outputPath
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables['DOPE_COMPANION_LAUNCH_KIND'] = 'Dev'

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw "Failed to start local dev executable at $exePath."
}

if ($Wait) {
    Wait-Process -Id $process.Id
}

[PSCustomObject]@{
    LaunchMode = 'LocalDevExe'
    ReusedProcess = $false
    ProcessId = $process.Id
    ExecutablePath = $exePath
}
