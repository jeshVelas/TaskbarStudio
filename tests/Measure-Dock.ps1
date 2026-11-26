[CmdletBinding()]
param(
    [ValidateRange(1, 100)][int]$Cycles = 50,
    [ValidateRange(1, 3600)][int]$Seconds = 600,
    [ValidateRange(0, 300)][int]$Warmup = 60,
    [ValidateRange(0, 100)][int]$ForcedCloses = 5,
    [switch]$CyclesOnly
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (!('DesktopProbe' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'DesktopProbe.cs') }
$exe = Join-Path $PSScriptRoot '..\src\FloatingDock\bin\Release\net10.0-windows\FloatingDock.exe'
$condition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'Cerrar')
$launcherCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'Bloc de notas')
function Wait-Button($process, $timer) {
    while ($timer.Elapsed.TotalSeconds -lt 12) {
        $process.Refresh()
        if ($process.HasExited) { throw "El dock terminó con código $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne 0) {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            $launcher = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $launcherCondition)
            if ($launcher -and $launcher.Current.IsEnabled -and $button -and $button.Current.IsEnabled) { return $button }
        }
        Start-Sleep -Milliseconds 15
    }
    throw 'No se encontró el botón Cerrar disponible.'
}
function Close-Button($button) {
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
# Peak engine usage grouped across all processes, including the desktop compositor.
function Get-Engines {
    $engines = @{}
    foreach ($counter in Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop) {
        if ($counter.Name -match '^pid_\d+_(.+)$') {
            $key = $Matches[1]
            $engines[$key] += [double]$counter.UtilizationPercentage
        }
    }
    if ($engines.Count -eq 0) { throw 'Sin contadores de motor GPU.' }
    return $engines
}
function Measure-Engines([int]$duration, $process) {
    $totals = @{}
    $samples = 0
    $peakMemory = 0
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process) {
            $process.Refresh()
            if ($process.HasExited) { throw 'El dock terminó durante la medición.' }
            $peakMemory = [Math]::Max([long]$peakMemory, $process.PrivateMemorySize64)
        }
        if ($script:gpuAvailable) {
            try {
                $engines = Get-Engines
                foreach ($key in $engines.Keys) { $totals[$key] += $engines[$key] }
                $samples++
            } catch {
                $script:gpuAvailable = $false
                Write-Warning "GPU pendiente: $_"
            }
        }
        Start-Sleep -Seconds 1
    } while ($clock.Elapsed.TotalSeconds -lt $duration)
    if ($samples) {
        foreach ($key in @($totals.Keys)) { $totals[$key] /= $samples }
    }
    return @{ Engines = $totals; PeakMemory = $peakMemory; Samples = $samples; Seconds = $clock.Elapsed.TotalSeconds }
}
$desktop = [DesktopProbe]::Snapshot()
Write-Output "Escritorio inicial: $desktop"
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
function Get-SessionDock {
    Get-Process -Name FloatingDock -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session
}
if (Get-SessionDock) { throw 'Ya hay un dock en esta sesión. La referencia requiere el escritorio sin dock.' }
function Write-SessionResources([int]$closed) {
    $globalHandle = [IntPtr]::new(-2) # GR_GLOBAL from WinUser.h
    $gdi = [DesktopProbe]::GetGuiResources($globalHandle, 0)
    $user = [DesktopProbe]::GetGuiResources($globalHandle, 1)
    Write-Output "Cerrados=$closed GDI_sesión=$gdi USER_sesión=$user"
}
Write-SessionResources 0
$times = @()
$resources = @()
for ($i = 1; $i -le $Cycles; $i++) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $dock = Start-Process -FilePath $exe -PassThru
    try {
        $button = Wait-Button $dock $timer
        $times += $timer.Elapsed.TotalMilliseconds
        $dock.Refresh()
        $resources += [pscustomobject]@{
            Handles = $dock.HandleCount
            GDI = [DesktopProbe]::GetGuiResources($dock.Handle, 0)
            USER = [DesktopProbe]::GetGuiResources($dock.Handle, 1)
        }
        if ($i -eq 1) {
            $dpi = [DesktopProbe]::GetDpiForWindow($dock.MainWindowHandle)
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($dock.MainWindowHandle)
            $conditionButtons = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            $names = @('Bloc de notas', 'Calculadora', 'Explorador', 'Cerrar')
            $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $conditionButtons)
            foreach ($name in $names) {
                $match = @($buttons | Where-Object { $_.Current.Name -eq $name })
                if ($match.Count -ne 1) { throw "Botón ausente o duplicado: $name" }
                $item = $match[0].Current
                if (!$item.IsEnabled -or $item.IsOffscreen -or $item.BoundingRectangle.IsEmpty -or
                    !$root.Current.BoundingRectangle.Contains($item.BoundingRectangle)) { throw "Botón fuera de la ventana o inactivo: $name" }
            }
            Write-Output "DPI_ventana=$dpi Botones_visibles_habilitados=$($names.Count)"
        }
        if ([DesktopProbe]::Snapshot() -ne $desktop) { throw 'Cambió el área u ocultación con el dock abierto.' }
        if ($i -le $ForcedCloses) { $dock.Kill() } else { Close-Button $button }
        if (!$dock.WaitForExit(5000)) { throw 'No terminó el proceso.' }
        if ($i -gt $ForcedCloses -and $dock.ExitCode -ne 0) { throw "Salida: $($dock.ExitCode)" }
        if ([DesktopProbe]::Snapshot() -ne $desktop) { throw 'Cambió el área u ocultación al cerrar.' }
    } finally {
        if (!$dock.HasExited) { $dock.Kill(); $dock.WaitForExit() }
        $dock.Dispose()
    }
    if ($i -in 1, [Math]::Ceiling($Cycles / 2), $Cycles) { Write-SessionResources $i }
}
if (Get-SessionDock) { throw 'Queda un proceso FloatingDock después de los ciclos.' }
Write-Output 'Procesos_residuales=0'
foreach ($metric in 'Handles','GDI','USER') {
    $range = $resources | Measure-Object -Property $metric -Minimum -Maximum
    if ($range.Minimum -le 0) { throw "Sin lectura válida de $metric" }
    Write-Output ("{0} primero={1} último={2} mínimo={3} máximo={4}" -f $metric,
        $resources[0].$metric, $resources[-1].$metric, $range.Minimum, $range.Maximum)
}
$sorted = @($times | Sort-Object)
Write-Output ('Ciclos={0} Forzados={1} Primero_ms={2:N0} P95_ms={3:N0}' -f $Cycles,
    [Math]::Min($Cycles,$ForcedCloses), $times[0], $sorted[[Math]::Ceiling($Cycles * 0.95) - 1])
if ($Cycles -gt 1) {
    $warm = @($times | Select-Object -Skip 1 | Sort-Object)
    Write-Output ('Posteriores={0} P95_posteriores_ms={1:N0}' -f $warm.Count, $warm[[Math]::Ceiling($warm.Count * 0.95) - 1])
}
if ($CyclesOnly) { return }
$script:gpuAvailable = $true
$baseline = Measure-Engines $Seconds $null
Write-Output ('Referencia_s={0:N1} Muestras_GPU={1}' -f $baseline.Seconds, $baseline.Samples)
$dock = Start-Process -FilePath $exe -ArgumentList '--load-test' -PassThru
try {
    $button = Wait-Button $dock ([Diagnostics.Stopwatch]::StartNew())
    Start-Sleep -Seconds $Warmup
    $dock.Refresh()
    $cpuStart = $dock.TotalProcessorTime.TotalSeconds
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $loaded = Measure-Engines $Seconds $dock
    $dock.Refresh()
    $cpu = 100 * ($dock.TotalProcessorTime.TotalSeconds - $cpuStart) / ($elapsed.Elapsed.TotalSeconds * [Environment]::ProcessorCount)
    Write-Output ('CPU_pct={0:N3} Privada_max_MiB={1:N2} Muestra_s={2:N1}' -f $cpu, ($loaded.PeakMemory / 1MB), $elapsed.Elapsed.TotalSeconds)
    if ($script:gpuAvailable) {
        $delta = 0.0
        foreach ($key in $loaded.Engines.Keys) {
            $delta = [Math]::Max($delta, $loaded.Engines[$key] - $baseline.Engines[$key])
        }
        Write-Output ('GPU_delta_max_pp={0:N3} Muestras_GPU={1}' -f $delta, $loaded.Samples)
    }
    if ([DesktopProbe]::Snapshot() -ne $desktop) { throw 'Cambió la barra durante la prueba de carga.' }
    Close-Button $button
    if (!$dock.WaitForExit(5000) -or $dock.ExitCode -ne 0) { throw 'Fallo al cerrar el dock de carga.' }
} finally {
    if (!$dock.HasExited) { $dock.Kill(); $dock.WaitForExit() }
    $dock.Dispose()
}
if ((Get-SessionDock) -or [DesktopProbe]::Snapshot() -ne $desktop) { throw 'El escritorio no volvió al estado inicial.' }
