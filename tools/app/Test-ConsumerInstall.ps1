[CmdletBinding()]
param(
    [string]$PackageName = "MesmerPrism.DopeCompanionPreview",
    [string]$OutputRoot = (Join-Path (Get-Location) "artifacts\verify\consumer-install-smoke"),
    [string]$DeviceSelector
)

$ErrorActionPreference = "Stop"

function Join-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    return ($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " "
}

function ConvertTo-ProcessArgument {
    param(
        [AllowNull()]
        [string]$Argument
    )

    if ($null -eq $Argument -or $Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            [void]$builder.Append('\' * (($backslashCount * 2) + 1))
            [void]$builder.Append('"')
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void]$builder.Append('\' * $backslashCount)
            $backslashCount = 0
        }

        [void]$builder.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void]$builder.Append('\' * ($backslashCount * 2))
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-ConsumerCli {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$WrapperPath,

        [Parameter(Mandatory = $true)]
        [string]$WorkspaceRoot,

        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory
    )

    $stdoutPath = Join-Path $OutputDirectory "$Name.out.txt"
    $stderrPath = Join-Path $OutputDirectory "$Name.err.txt"
    $processArguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $WrapperPath
    ) + $Arguments

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "powershell.exe"
    $startInfo.WorkingDirectory = $WorkspaceRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = Join-ProcessArguments $processArguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    Set-Content -Path $stdoutPath -Value $stdout -NoNewline
    Set-Content -Path $stderrPath -Value $stderr -NoNewline

    if ($process.ExitCode -ne 0) {
        throw "CLI command '$Name' failed with exit code $($process.ExitCode). stderr: $stderr"
    }

    return $stdout
}

function Assert-ContainsText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Text -notlike "*$Expected*") {
        throw "$Name did not contain '$Expected'."
    }
}

function Assert-ProfileField {
    param(
        [Parameter(Mandatory = $true)]
        $Fields,

        [Parameter(Mandatory = $true)]
        [string]$Id
    )

    $match = @($Fields | Where-Object { $_.id -eq $Id -or $_.Id -eq $Id })
    if ($match.Count -eq 0) {
        throw "Field catalog did not include '$Id'."
    }
}

function Get-CompatibleHashByTag {
    param(
        [Parameter(Mandatory = $true)]
        $Compatibility,

        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    $entry = @($Compatibility.apps | Where-Object { $_.tags -contains $Tag } | Select-Object -First 1)
    if ($entry.Count -eq 0) {
        throw "Compatibility manifest did not include tag '$Tag'."
    }

    return [string]$entry[0].sha256
}

if (Test-Path $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$OutputRoot = (Resolve-Path $OutputRoot).Path

$package = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue |
    Sort-Object Version -Descending |
    Select-Object -First 1
if ($null -eq $package) {
    throw "Package '$PackageName' is not installed."
}

$operatorDataRoot = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalCache\Local\DopeCompanion"
$workspaceRoot = Join-Path $operatorDataRoot "agent-workspace"
$wrapperPath = Join-Path $workspaceRoot "dope-companion.ps1"
$cliPath = Join-Path $workspaceRoot "cli\current\dope-companion.exe"
if (-not (Test-Path $wrapperPath)) {
    throw "Packaged agent-workspace CLI wrapper was not found at '$wrapperPath'. Open the packaged app once so it can refresh the local workspace mirror."
}

if (-not (Test-Path $cliPath)) {
    throw "Packaged agent-workspace CLI executable was not found at '$cliPath'. Open the packaged app once so it can refresh the local workspace mirror."
}

$process = Get-Process -Name "DopeCompanion" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$($package.InstallLocation)*" } |
    Select-Object -First 1

$help = Invoke-ConsumerCli `
    -Name "help" `
    -WrapperPath $wrapperPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("--help")
Assert-ContainsText -Name "CLI help" -Text $help -Expected "DopeCompanion"

$catalog = Invoke-ConsumerCli `
    -Name "catalog-list" `
    -WrapperPath $wrapperPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("catalog", "list")
Assert-ContainsText -Name "Catalog list" -Text $catalog -Expected "dope-projected-feed-colorama"
Assert-ContainsText -Name "Catalog list" -Text $catalog -Expected "rusty-dope-colorama-feedback-border"

$hotload = Invoke-ConsumerCli `
    -Name "hotload-list" `
    -WrapperPath $wrapperPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("hotload", "list")
Assert-ContainsText -Name "Hotload list" -Text $hotload -Expected "dope_projected_feed_colorama_baseline"
Assert-ContainsText -Name "Hotload list" -Text $hotload -Expected "rusty_dope_colorama_feedback_border_soft"

$studyHelp = Invoke-ConsumerCli `
    -Name "study-help" `
    -WrapperPath $wrapperPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("study", "--help")
Assert-ContainsText -Name "Study help" -Text $studyHelp -Expected "diagnostics-report"
Assert-ContainsText -Name "Study help" -Text $studyHelp -Expected "run-harness"

$visualFields = Invoke-ConsumerCli `
    -Name "dope-visual-fields" `
    -WrapperPath $wrapperPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("dope", "visual", "fields", "--json") |
    ConvertFrom-Json
Assert-ProfileField -Fields $visualFields -Id "tracers_enabled"
Assert-ProfileField -Fields $visualFields -Id "sphere_radius_max"

$controllerFields = Invoke-ConsumerCli `
    -Name "dope-controller-fields" `
    -WrapperPath $wrapperPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("dope", "controller", "fields", "--json") |
    ConvertFrom-Json
Assert-ProfileField -Fields $controllerFields -Id "median_window"

$tooling = Invoke-ConsumerCli `
    -Name "tooling-status" `
    -WrapperPath $wrapperPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("tooling", "status")
Assert-ContainsText -Name "Tooling status" -Text $tooling -Expected "Quest control ready"

$sessionKitRoot = Join-Path $workspaceRoot "samples\quest-session-kit"
$compatibilityPath = Join-Path $sessionKitRoot "APKs\compatibility.json"
$compatibility = Get-Content -Raw $compatibilityPath | ConvertFrom-Json
$unityApkPath = Join-Path $sessionKitRoot "APKs\DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk"
$rustyApkPath = Join-Path $sessionKitRoot "APKs\RustyDOPE-ColoramaFeedbackBorder.apk"
$unityHash = (Get-FileHash -Algorithm SHA256 $unityApkPath).Hash
$rustyHash = (Get-FileHash -Algorithm SHA256 $rustyApkPath).Hash
$expectedUnityHash = Get-CompatibleHashByTag -Compatibility $compatibility -Tag "projected-feed"
$expectedRustyHash = Get-CompatibleHashByTag -Compatibility $compatibility -Tag "rust"
if ($unityHash -ne $expectedUnityHash) {
    throw "Unity APK hash expected '$expectedUnityHash' but was '$unityHash'."
}

if ($rustyHash -ne $expectedRustyHash) {
    throw "Rusty APK hash expected '$expectedRustyHash' but was '$rustyHash'."
}

$deviceStatus = $null
$studyStatus = $null
if (-not [string]::IsNullOrWhiteSpace($DeviceSelector)) {
    $deviceStatus = Invoke-ConsumerCli `
        -Name "device-status" `
        -WrapperPath $wrapperPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("status", "-d", $DeviceSelector)
    $studyStatus = Invoke-ConsumerCli `
        -Name "study-status" `
        -WrapperPath $wrapperPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("study", "status", "dope-projected-feed-colorama", "-d", $DeviceSelector)
    Assert-ContainsText -Name "Study status" -Text $studyStatus -Expected "DOPE Projected Feed Colorama APK"
}

$summary = [pscustomobject]@{
    PackageName = $package.Name
    PackageVersion = $package.Version.ToString()
    PackageFamilyName = $package.PackageFamilyName
    InstallLocation = $package.InstallLocation
    RunningProcessId = if ($null -eq $process) { $null } else { $process.Id }
    RunningWindowTitle = if ($null -eq $process) { $null } else { $process.MainWindowTitle }
    OperatorDataRoot = $operatorDataRoot
    WorkspaceRoot = $workspaceRoot
    CliPath = $cliPath
    WrapperPath = $wrapperPath
    VisualFieldCount = @($visualFields).Count
    ControllerFieldCount = @($controllerFields).Count
    UnityApkSha256 = $unityHash
    RustyApkSha256 = $rustyHash
    DeviceStatusChecked = -not [string]::IsNullOrWhiteSpace($DeviceSelector)
}

$summaryPath = Join-Path $OutputRoot "summary.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath
$summary | ConvertTo-Json -Depth 8
