param(
    [Parameter(Mandatory = $true)][string]$Lib,
    [string]$Repo,
    [string]$OutDir = (Join-Path $env:TEMP 'semispec-shots'),
    [string]$AppPort = 'COM3',
    [string]$EmulatorPort = 'COM4',
    [int]$FillSeconds = 270
)
# Renders the three readme screenshots of SemiSpec from a cold start against the board emulator.
# -Lib is the window-capture helper library it dot-sources. Needs a Release build, a com0com pair
# AppPort<->EmulatorPort, default display settings and no running SemiSpec. Writes to -OutDir;
# copy the PNGs to docs/img only after looking at them.
. $Lib
Assert-CaptureHost
# PowerShell 5 leaves $PSScriptRoot empty inside param defaults, so the repository root is resolved here.
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
if (Get-Process -Name SemiSpec -ErrorAction SilentlyContinue) { throw 'SemiSpec is already running; close it first.' }
$settings = @("C:\DISTR\Config\SemiSpec\settings.yaml", (Join-Path $env:LOCALAPPDATA 'SemiSpec\Config\settings.yaml'))
foreach ($file in $settings) {
  if ((Test-Path $file) -and (Select-String -Path $file -Pattern ':\s*true' -Quiet)) { throw "$file turns on a display choice; the readme shows the defaults." }
}
$bin = Join-Path $Repo 'SemiSpec\Artifacts\bin'
$appExe = Join-Path $bin 'SemiSpec.UI\release\SemiSpec.exe'
$emulatorExe = Join-Path $bin 'SemiSpec.Tools.BoardEmulator\release\SemiSpec.Tools.BoardEmulator.exe'
foreach ($exe in @($appExe, $emulatorExe)) { if (-not (Test-Path $exe)) { throw "missing $exe; run dotnet build SemiSpec.slnx -c Release" } }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$emulator = $null; $app = $null
try {
  $emulator = Start-Process -PassThru -WindowStyle Hidden -FilePath $emulatorExe `
    -ArgumentList @('--port', $EmulatorPort, '--rate', '2', '--dark-every', '20') `
    -RedirectStandardOutput "$OutDir\emulator.out" -RedirectStandardError "$OutDir\emulator.err"
  $app = Start-Process -PassThru -FilePath $appExe
  $main = Wait-AppWindow $app
  Start-Sleep -Seconds 2
  Set-ClientSize $main 1280 800
  $root = Get-Root $main
  Select-ComboItem $root 'PortList' $AppPort
  Invoke-Element $root 'Connect'
  Start-Sleep -Seconds 5

  # Scenes 1 and 2 show no message panel; a clean connect leaves it hidden, a fault opens it.
  if (Test-Shown $root 'ClearMessages') {
    Invoke-Element $root 'ClearMessages'
    Send-ElementClick $app.Id $main 'ViewMenu'; Start-Sleep -Milliseconds 700
    Send-ElementClick $app.Id $main 'ViewMessagePanel'; Start-Sleep -Milliseconds 700
  }
  if (Test-Shown $root 'ClearMessages') { throw 'the message panel is still shown; the scene coordinates assume it hidden' }
  Start-Sleep -Seconds $FillSeconds

  # Scene 1: the time panel. At the work area's bottom it opens above the time axis instead of below.
  Move-WindowToBottom $main
  Send-Click $main 640 690; Start-Sleep -Milliseconds 1200
  Save-Composite $app.Id $main "$OutDir\axis-flyout.png" 1
  Send-Click $main 1100 320; Start-Sleep -Milliseconds 600

  # Scene 2: a click on the trend's data area shows that moment's spectrum.
  Send-Click $main 1000 600; Start-Sleep -Milliseconds 1500
  Save-Composite $app.Id $main "$OutDir\past-moment.png" 0

  # Scene 3: back to live, the board stops, and after 10 s it does not respond.
  Invoke-Element $root 'BackToLive'
  Stop-Started $emulator.Id
  Start-Sleep -Seconds 13
  if (-not (Test-Shown $root 'ClearMessages')) { throw 'the board-not-responding message did not open the panel' }
  Save-Composite $app.Id $main "$OutDir\board-not-responding.png" 0
  "done: $OutDir"
}
finally {
  if ($app) { Stop-Started $app.Id }
  if ($emulator) { Stop-Started $emulator.Id }
}
