param(
    [int]$DurationSeconds = 120,
    [int]$PollIntervalMs = 250
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sessionRoot = Join-Path $repoRoot 'artifacts\watch\companion-stop-issue'
$sessionId = Get-Date -Format 'yyyyMMdd-HHmmss'
$sessionDir = Join-Path $sessionRoot $sessionId
$screenshotDir = Join-Path $sessionDir 'screenshots'
$statePath = Join-Path $sessionDir 'state.jsonl'
$eventPath = Join-Path $sessionDir 'events.jsonl'
$metadataPath = Join-Path $sessionDir 'session.json'
$latestPath = Join-Path $sessionRoot 'latest-session.json'
$stopFile = Join-Path $sessionDir 'stop.txt'

New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class StopIssueWatcherNativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

function Get-TopLevelWindows {
    $windows = [System.Collections.Generic.List[object]]::new()
    $foregroundHandle = [StopIssueWatcherNativeMethods]::GetForegroundWindow()
    $delegate = [StopIssueWatcherNativeMethods+EnumWindowsProc]{
        param($handle, $lParam)

        $processId = [uint32]0
        [void][StopIssueWatcherNativeMethods]::GetWindowThreadProcessId($handle, [ref]$processId)

        $titleLength = [StopIssueWatcherNativeMethods]::GetWindowTextLength($handle)
        $builder = New-Object System.Text.StringBuilder ($titleLength + 1)
        [void][StopIssueWatcherNativeMethods]::GetWindowText($handle, $builder, $builder.Capacity)

        $rect = New-Object StopIssueWatcherNativeMethods+RECT
        [void][StopIssueWatcherNativeMethods]::GetWindowRect($handle, [ref]$rect)

        $style = [StopIssueWatcherNativeMethods]::GetWindowLong($handle, -16)
        $exStyle = [StopIssueWatcherNativeMethods]::GetWindowLong($handle, -20)

        $windows.Add([pscustomobject]@{
            Handle = ('0x{0:X}' -f $handle.ToInt64())
            ProcessId = $processId
            Title = if ($builder.Length -gt 0) { $builder.ToString() } else { '<no title>' }
            Visible = [StopIssueWatcherNativeMethods]::IsWindowVisible($handle)
            Enabled = [StopIssueWatcherNativeMethods]::IsWindowEnabled($handle)
            Minimized = [StopIssueWatcherNativeMethods]::IsIconic($handle)
            Hung = [StopIssueWatcherNativeMethods]::IsHungAppWindow($handle)
            Foreground = ($handle -eq $foregroundHandle)
            Bounds = [pscustomobject]@{
                Left = $rect.Left
                Top = $rect.Top
                Width = $rect.Right - $rect.Left
                Height = $rect.Bottom - $rect.Top
            }
            Style = ('0x{0:X8}' -f ($style -band 0xffffffffL))
            ExStyle = ('0x{0:X8}' -f ($exStyle -band 0xffffffffL))
        }) | Out-Null

        return $true
    }

    [void][StopIssueWatcherNativeMethods]::EnumWindows($delegate, [IntPtr]::Zero)
    return $windows
}

function Save-Screenshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $virtualScreen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $virtualScreen.Width, $virtualScreen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.CopyFromScreen(
            $virtualScreen.Left,
            $virtualScreen.Top,
            0,
            0,
            $bitmap.Size)

        $fileName = '{0}-{1}.png' -f (Get-Date -Format 'HHmmss-fff'), $Label
        $path = Join-Path $screenshotDir $fileName
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        return $path
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-JsonLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $Value | ConvertTo-Json -Depth 8 -Compress | Add-Content -Path $Path -Encoding UTF8
}

function New-Snapshot {
    $timestamp = [DateTimeOffset]::Now
    $allWindows = @(Get-TopLevelWindows)
    $visibleWindows = @($allWindows | Where-Object Visible)
    $dopeProcesses = @(Get-Process DopeCompanion -ErrorAction SilentlyContinue)
    $dopeProcess = $dopeProcesses | Select-Object -First 1
    $dopeProcessId = if ($dopeProcess) { [uint32]$dopeProcess.Id } else { $null }
    $dopeWindows = if ($dopeProcessId) {
        @($allWindows | Where-Object ProcessId -EQ $dopeProcessId)
    }
    else {
        @()
    }

    $foreground = $allWindows | Where-Object Foreground | Select-Object -First 1
    $clickToDo = $visibleWindows | Where-Object Title -EQ 'Click to Do' | Select-Object -First 1
    $signalWindow = $visibleWindows | Where-Object Title -EQ 'Signal' | Select-Object -First 1
    $visibleDopeWindows = @($dopeWindows | Where-Object Visible)
    $visibleDopeTitles = @($visibleDopeWindows | ForEach-Object Title)

    $zOrderTitles = @(
        $visibleWindows |
            Where-Object {
                $_.Title -like 'DOPE*' -or
                $_.Title -eq 'Click to Do' -or
                $_.Title -eq 'Signal'
            } |
            ForEach-Object Title
    )

    $firstVisibleDopeIndex = [Array]::IndexOf($zOrderTitles, ($visibleDopeTitles | Select-Object -First 1))
    $clickToDoIndex = [Array]::IndexOf($zOrderTitles, 'Click to Do')

    return [pscustomobject]@{
        Timestamp = $timestamp.ToString('o')
        DopeProcess = if ($dopeProcess) {
            [pscustomobject]@{
                Id = $dopeProcess.Id
                Responding = $dopeProcess.Responding
                MainWindowTitle = $dopeProcess.MainWindowTitle
                Path = $dopeProcess.Path
            }
        } else { $null }
        Foreground = $foreground
        ClickToDo = $clickToDo
        Signal = $signalWindow
        DopeWindows = $dopeWindows
        VisibleDopeWindows = $visibleDopeWindows
        ZOrderTitles = $zOrderTitles
        ClickToDoAboveDope = (
            $clickToDoIndex -ge 0 -and
            $firstVisibleDopeIndex -ge 0 -and
            $clickToDoIndex -lt $firstVisibleDopeIndex)
        CastSurfaceVisible = @(
            $visibleDopeWindows |
                Where-Object {
                    $_.Title -like 'DOPE Companion Cast*' -or
                    $_.Title -like 'DOPE Companion Render View*'
                }
        ).Count -gt 0
    }
}

function Write-EventRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Kind,
        [Parameter(Mandatory = $true)]
        [object]$Snapshot,
        [string[]]$Screenshots = @()
    )

    Write-JsonLine -Path $eventPath -Value ([pscustomobject]@{
        Timestamp = [DateTimeOffset]::Now.ToString('o')
        Kind = $Kind
        ForegroundTitle = $Snapshot.Foreground.Title
        ClickToDoAboveDope = $Snapshot.ClickToDoAboveDope
        CastSurfaceVisible = $Snapshot.CastSurfaceVisible
        VisibleDopeTitles = @($Snapshot.VisibleDopeWindows | ForEach-Object Title)
        Screenshots = $Screenshots
    })
}

$metadata = [ordered]@{
    SessionId = $sessionId
    StartedAt = (Get-Date).ToString('o')
    DurationSeconds = $DurationSeconds
    PollIntervalMs = $PollIntervalMs
    SessionDir = $sessionDir
    ScreenshotDir = $screenshotDir
    StatePath = $statePath
    EventPath = $eventPath
    StopFile = $stopFile
}

$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataPath -Encoding UTF8
$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $latestPath -Encoding UTF8

$initialScreenshot = Save-Screenshot -Label 'initial'
$previousSnapshot = $null
$previousSignature = $null

Write-EventRecord -Kind 'watcher-started' -Snapshot (New-Snapshot) -Screenshots @($initialScreenshot)

$deadline = (Get-Date).AddSeconds($DurationSeconds)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $stopFile) {
        break
    }

    $snapshot = New-Snapshot
    Write-JsonLine -Path $statePath -Value $snapshot

    $signature = [pscustomobject]@{
        Foreground = $snapshot.Foreground.Title
        ClickToDoAboveDope = $snapshot.ClickToDoAboveDope
        CastSurfaceVisible = $snapshot.CastSurfaceVisible
        VisibleDopeTitles = @($snapshot.VisibleDopeWindows | ForEach-Object Title)
        ZOrderTitles = $snapshot.ZOrderTitles
    } | ConvertTo-Json -Depth 6 -Compress

    if ($signature -ne $previousSignature) {
        $screenshot = Save-Screenshot -Label 'state-change'
        Write-EventRecord -Kind 'state-change' -Snapshot $snapshot -Screenshots @($screenshot)
        $previousSignature = $signature
    }

    if ($previousSnapshot -and $previousSnapshot.CastSurfaceVisible -and -not $snapshot.CastSurfaceVisible) {
        $screenshots = @(
            (Save-Screenshot -Label 'cast-surface-closed')
        )
        Start-Sleep -Milliseconds 500
        $screenshots += Save-Screenshot -Label 'post-stop-500ms'
        Start-Sleep -Milliseconds 1000
        $screenshots += Save-Screenshot -Label 'post-stop-1500ms'
        Write-EventRecord -Kind 'cast-surface-closed' -Snapshot $snapshot -Screenshots $screenshots
    }

    if ($snapshot.ClickToDoAboveDope -and (-not $previousSnapshot -or -not $previousSnapshot.ClickToDoAboveDope)) {
        $screenshots = @(
            (Save-Screenshot -Label 'click-to-do-above-dope')
        )
        Write-EventRecord -Kind 'click-to-do-blocking' -Snapshot $snapshot -Screenshots $screenshots
    }

    if ($snapshot.DopeProcess -and
        $snapshot.VisibleDopeWindows.Count -gt 0 -and
        $snapshot.Foreground -and
        $snapshot.Foreground.Title -notlike 'DOPE*' -and
        (-not $snapshot.CastSurfaceVisible)) {
        $screenshots = @(
            (Save-Screenshot -Label 'dope-backgrounded-after-stop')
        )
        Write-EventRecord -Kind 'dope-not-foreground-after-stop' -Snapshot $snapshot -Screenshots $screenshots
    }

    $previousSnapshot = $snapshot
    Start-Sleep -Milliseconds $PollIntervalMs
}

$finalSnapshot = New-Snapshot
$finalScreenshot = Save-Screenshot -Label 'final'
Write-JsonLine -Path $statePath -Value $finalSnapshot
Write-EventRecord -Kind (if (Test-Path $stopFile) { 'watcher-stopped' } else { 'watcher-finished' }) -Snapshot $finalSnapshot -Screenshots @($finalScreenshot)

Write-Output "SessionDir: $sessionDir"
Write-Output "StatePath: $statePath"
Write-Output "EventPath: $eventPath"
Write-Output "StopFile: $stopFile"
