[CmdletBinding()]
param([switch]$ExerciseChange)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (!('DesktopProbe' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'DesktopProbe.cs') }
$exe = Join-Path $PSScriptRoot '..\src\NativeTrial\bin\Release\net10.0-windows\NativeTrial.exe'
$pending = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TaskbarStudio\NativeTrial\pending.v1'
if (Test-Path -LiteralPath $pending) { throw 'Resolve the existing trial before running this test.' }
if (Get-Process NativeTrial -ErrorAction SilentlyContinue) { throw 'Close the existing trial first.' }
function Find-Element($window, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-Element($window, [string]$name) {
    $limit = [Diagnostics.Stopwatch]::StartNew()
    do {
        $element = Find-Element $window $name
        if ($element) { return $element }
        Start-Sleep -Milliseconds 40
    } while ($limit.Elapsed.TotalSeconds -lt 5)
    throw "Missing element: $name"
}
function Invoke-Control($control) {
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
$trial = Start-Process -FilePath $exe -PassThru
$changeStarted = $false
try {
    $limit = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($trial.HasExited) { throw 'NativeTrial exited early.' }
        $trial.Refresh()
        if ($trial.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 30
    } while ($limit.Elapsed.TotalSeconds -lt 8)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($trial.MainWindowHandle)
    Invoke-Control (Wait-Element $root 'Consultar estado')
    $on = Wait-Element $root 'Activar ocultación'
    $off = Wait-Element $root 'Desactivar ocultación'
    $snapshot = [DesktopProbe]::Snapshot()
    $enabled = $snapshot.EndsWith('autohide=True')
    $label = 'Ocultación automática · '
    $initial = if ($enabled) { 'activada' } else { 'desactivada' }
    $opposite = if ($enabled) { 'desactivada' } else { 'activada' }
    $null = Wait-Element $root ($label + $initial)
    Write-Output "Initial $snapshot"
    $guarded = !$on.Current.IsEnabled -and !$off.Current.IsEnabled
    if ($guarded) {
        $null = Wait-Element $root 'En la compilación 26200, la prueba de ocultación requiere la barra en el borde inferior.'
        if (Test-Path -LiteralPath $pending) { throw 'Unexpected recovery journal.' }
        Write-Output 'Position guard verified. Write unavailable on this edge.'
    } elseif ($off.Current.IsEnabled -ne $enabled -or $on.Current.IsEnabled -eq $enabled) {
        throw 'Initial controls differ from Shell.'
    }
    if ($ExerciseChange -and !$guarded) {
        $changeStarted = $true
        if ($enabled) { Invoke-Control $off } else { Invoke-Control $on }
        $null = Wait-Element $root ($label + $opposite)
        if (![DesktopProbe]::Snapshot().EndsWith("autohide=$(!$enabled)")) { throw 'Shell change unconfirmed.' }
        if (!(Test-Path -LiteralPath $pending)) { throw 'Missing recovery journal.' }
        Invoke-Control (Wait-Element $root 'Restaurar estado anterior')
        $null = Wait-Element $root ($label + $initial)
        if (![DesktopProbe]::Snapshot().EndsWith("autohide=$enabled")) { throw 'Shell restoration unconfirmed.' }
        if (Test-Path -LiteralPath $pending) { throw 'Recovery journal still present.' }
        Write-Output 'Observed verified change and restoration.'
    }
    Invoke-Control (Wait-Element $root 'Cerrar')
    if (!$trial.WaitForExit(5000) -or $trial.ExitCode -ne 0) { throw 'Unclean exit.' }
    Write-Output 'Closed successfully.'
} finally {
    if (!$trial.HasExited) {
        if ($changeStarted -and (Test-Path -LiteralPath $pending)) {
            $labels = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
            Write-Warning ("Estado al fallar: " + [DesktopProbe]::Snapshot() + " | " + ($labels -join ' | '))
            Invoke-Control (Wait-Element $root 'Restaurar estado anterior')
            $null = Wait-Element $root ($label + $initial)
            $limit.Restart()
            while ((Test-Path -LiteralPath $pending) -and $limit.Elapsed.TotalSeconds -lt 5) {
                Start-Sleep -Milliseconds 40
            }
            if ((Test-Path -LiteralPath $pending) -or ![DesktopProbe]::Snapshot().EndsWith("autohide=$enabled")) {
                throw 'Inspect recovery in the open trial window.'
            }
            Write-Output ("Recovery verified, journal cleared. " + [DesktopProbe]::Snapshot())
        }
        $null = $trial.CloseMainWindow()
        if (!$trial.WaitForExit(5000)) { Write-Warning 'Inspect the trial window and recovery status.' }
    }
    $trial.Dispose()
}
