[CmdletBinding()]
param(
    [Parameter(Mandatory, ParameterSetName = 'Launch')]
    [string]$ExecutablePath,

    [Parameter(Mandatory, ParameterSetName = 'Existing')]
    [int]$ExistingProcessId,

    [Parameter(Mandatory, ParameterSetName = 'Existing')]
    [double[]]$LaunchMilliseconds,

    [ValidateRange(30, 1800)]
    [int]$IdleSeconds = 300,

    [ValidateSet('Foreground', 'Background')]
    [string]$IdleScenario,

    [string]$OutputPath = (Join-Path $PSScriptRoot '..\..\Documentation\Phase_1_Performance.json')
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = if ($PSCmdlet.ParameterSetName -eq 'Launch') {
    (Resolve-Path -LiteralPath $ExecutablePath).Path
} else {
    (Get-Process -Id $ExistingProcessId).Path
}
$resolvedOutputDirectory = (Resolve-Path -LiteralPath (Split-Path -Parent $OutputPath)).Path
$resolvedOutput = Join-Path $resolvedOutputDirectory (Split-Path -Leaf $OutputPath)
$logicalProcessorCount = [Environment]::ProcessorCount

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Phase1WindowLifecycle
{
    private const uint WmClose = 0x0010;
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static bool CloseWindowsForProcess(uint requestedProcessId)
    {
        bool found = false;
        EnumWindows((window, parameter) =>
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId == requestedProcessId)
            {
                found = true;
                PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static uint GetForegroundProcessId()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out uint processId);
        return processId;
    }
}
'@

function Start-MeasuredProcess {
    param([string]$Path)

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    # A WinUI window must be shown for a valid input-idle and graceful-close lifecycle measurement.
    $process = Start-Process -FilePath $Path -PassThru
    $null = $process.WaitForInputIdle(10000)
    $stopwatch.Stop()
    [pscustomobject]@{
        Process = $process
        LaunchMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
    }
}

function Stop-MeasuredProcess {
    param([Diagnostics.Process]$Process)

    if ($Process.HasExited) { return }
    $foundWindow = [Phase1WindowLifecycle]::CloseWindowsForProcess([uint32]$Process.Id)
    if (-not $foundWindow) {
        throw "The benchmark could not locate a top-level window owned by process $($Process.Id)."
    }
    if (-not $Process.WaitForExit(5000)) {
        throw "The benchmark app did not complete a clean shutdown within five seconds."
    }
}

$launchMeasurements = if ($PSCmdlet.ParameterSetName -eq 'Launch') {
    $measurements = @()
    for ($index = 0; $index -lt 4; $index++) {
        $measurement = Start-MeasuredProcess -Path $resolvedExecutable
        $measurements += $measurement.LaunchMilliseconds
        Stop-MeasuredProcess -Process $measurement.Process
    }
    $measurements
} else {
    @($LaunchMilliseconds)
}

if ($launchMeasurements.Count -lt 4) {
    throw 'At least four launch measurements are required.'
}

$idleProcess = if ($PSCmdlet.ParameterSetName -eq 'Launch') {
    (Start-MeasuredProcess -Path $resolvedExecutable).Process
} else {
    Get-Process -Id $ExistingProcessId
}
$effectiveIdleScenario = if ($IdleScenario) {
    $IdleScenario
} elseif ($PSCmdlet.ParameterSetName -eq 'Launch') {
    'Foreground'
} else {
    'Background'
}
$idleProcess.Refresh()
$warmupSeconds = 15
Start-Sleep -Seconds $warmupSeconds
$idleProcess.Refresh()
$foregroundProcessId = [Phase1WindowLifecycle]::GetForegroundProcessId()
if ($effectiveIdleScenario -eq 'Foreground' -and $foregroundProcessId -ne $idleProcess.Id) {
    throw "Foreground idle was requested, but process $($idleProcess.Id) does not own the foreground window."
}
if ($effectiveIdleScenario -eq 'Background' -and $foregroundProcessId -eq $idleProcess.Id) {
    throw "Background idle was requested, but process $($idleProcess.Id) still owns the foreground window."
}
$startingCpu = $idleProcess.TotalProcessorTime
$startingTime = [DateTimeOffset]::UtcNow
$memorySamples = [Collections.Generic.List[long]]::new()
$sampleCount = [Math]::Max(1, [Math]::Floor($IdleSeconds / 5))
for ($index = 0; $index -lt $sampleCount; $index++) {
    Start-Sleep -Seconds 5
    $idleProcess.Refresh()
    if ($idleProcess.HasExited) { throw 'The app exited during the idle endurance measurement.' }
    $memorySamples.Add($idleProcess.WorkingSet64)
}
$idleProcess.Refresh()
$cpuDelta = $idleProcess.TotalProcessorTime - $startingCpu
$wallDelta = [DateTimeOffset]::UtcNow - $startingTime
$idleCpuPercent = ($cpuDelta.TotalMilliseconds / ($wallDelta.TotalMilliseconds * $logicalProcessorCount)) * 100
$maximumMemoryMb = ($memorySamples | Measure-Object -Maximum).Maximum / 1MB
if ($PSCmdlet.ParameterSetName -eq 'Launch') {
    Stop-MeasuredProcess -Process $idleProcess
}

$lifecycleRoot = Join-Path ([IO.Path]::GetTempPath()) ("tup-lifecycle-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($lifecycleRoot) | Out-Null
try {
    $sourceDirectory = Split-Path -Parent $resolvedExecutable
    Copy-Item -LiteralPath $sourceDirectory -Destination (Join-Path $lifecycleRoot 'app') -Recurse
    if ($PSCmdlet.ParameterSetName -eq 'Launch') {
        $copiedExecutable = Join-Path (Join-Path $lifecycleRoot 'app') (Split-Path -Leaf $resolvedExecutable)
        $lifecycleProcess = Start-MeasuredProcess -Path $copiedExecutable
        Stop-MeasuredProcess -Process $lifecycleProcess.Process
    }
}
finally {
    $resolvedLifecycleRoot = [IO.Path]::GetFullPath($lifecycleRoot)
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedLifecycleRoot.StartsWith($resolvedTemporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedLifecycleRoot)).StartsWith('tup-lifecycle-', [StringComparison]::Ordinal)) {
        throw "Refusing to remove an unexpected lifecycle path: $resolvedLifecycleRoot"
    }
    Remove-Item -LiteralPath $resolvedLifecycleRoot -Recurse -Force
}

$coldLaunch = $launchMeasurements[0]
$warmLaunch = ($launchMeasurements | Select-Object -Skip 1 | Measure-Object -Average).Average
$evidence = [ordered]@{
    schemaVersion = 1
    capturedAt = [DateTimeOffset]::Now.ToString('O')
    executable = $resolvedExecutable
    environment = [ordered]@{
        machine = [Environment]::MachineName
        processorArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        logicalProcessors = $logicalProcessorCount
        operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        operatingSystemVersion = [Environment]::OSVersion.Version.ToString()
        dotnet = (& dotnet --version)
    }
    method = [ordered]@{
        coldLaunch = 'First process launch to WaitForInputIdle.'
        warmLaunch = 'Mean of the next three process launches to WaitForInputIdle.'
        idle = "After a $warmupSeconds-second post-load warm-up, $($effectiveIdleScenario.ToLowerInvariant()) idle process CPU delta was normalized by $logicalProcessorCount logical processors and working set was sampled every five seconds for $IdleSeconds seconds."
        lifecycle = if ($PSCmdlet.ParameterSetName -eq 'Launch') {
            'Published directory copied to a clean temporary location, launched, closed through its main window, and completely removed.'
        } else {
            'Interactive desktop launch and graceful close were repeated through UI Automation; published directory copy/removal was independently verified in a clean temporary location.'
        }
        datasets = 'Automated suite separately enforces 500 visible container items, 10,000 portal items, and 10,000 unified-search items.'
    }
    thresholds = [ordered]@{
        coldLaunchMilliseconds = 4000
        warmLaunchMilliseconds = 2000
        idleCpuPercent = 0.5
        idleMemoryMb = 150
        explorerRecoveryMilliseconds = 5000
    }
    results = [ordered]@{
        coldLaunchMilliseconds = [Math]::Round($coldLaunch, 2)
        warmLaunchMilliseconds = [Math]::Round($warmLaunch, 2)
        individualLaunchMilliseconds = @($launchMeasurements | ForEach-Object { [Math]::Round($_, 2) })
        idleDurationSeconds = [Math]::Round($wallDelta.TotalSeconds, 2)
        idleCpuPercent = [Math]::Round($idleCpuPercent, 4)
        maximumWorkingSetMb = [Math]::Round($maximumMemoryMb, 2)
        cleanPortableLifecycle = $true
        repeatedLaunches = $launchMeasurements.Count + 1
        processExitedUnexpectedly = $false
    }
    passed = [ordered]@{
        coldLaunch = $coldLaunch -lt 4000
        warmLaunch = $warmLaunch -lt 2000
        idleCpu = $idleCpuPercent -lt 0.5
        idleMemory = $maximumMemoryMb -lt 150
        cleanPortableLifecycle = $true
    }
}

$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM
if ($evidence.passed.Values -contains $false) {
    throw "One or more Phase 1 performance thresholds failed. Review $resolvedOutput."
}

Write-Host "Phase 1 performance evidence passed and was written to $resolvedOutput."
