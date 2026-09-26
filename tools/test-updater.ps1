# End-to-end test of Settings > Check for updates, driven through UI
# Automation. Build a copy that calls itself older than the newest GitHub
# release, start it, then run this elevated (the app is):
#   dotnet build -c Release -p:Version=4.0.0
#   Start-Process bin\Release\net8.0-windows\UnboundKeys-v4.0.0.exe
#   Start-Process powershell -Verb RunAs -ArgumentList "-ExecutionPolicy","Bypass","-File","tools\test-updater.ps1"
# It clicks Check, then Download and install, and watches the handover to
# the new process. The log lands in %TEMP%\unboundkeys-updater-test.log.
# Afterwards close the new copy and delete the folder it unpacked into
# (bin\Release\UnboundKeys-v<new>). ASCII only: Windows PowerShell reads
# this file as ANSI.
param([string]$OldName = "UnboundKeys-v4.0.0", [string]$NewName = "UnboundKeys-v4.0.1")
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP "unboundkeys-updater-test.log"
"" | Set-Content $log
function Log($m) { Add-Content -Path $log -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $m) }

try {
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$raw = [System.Windows.Automation.TreeWalker]::RawViewWalker
$buttonId = [System.Windows.Automation.ControlType]::Button.Id

$proc = Get-Process $OldName | Select-Object -First 1
if ($null -eq $proc) { throw "$OldName is not running" }

function Get-Window($name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
  foreach ($w in $AE::RootElement.FindAll($TS::Children, $cond)) { if ($w.Current.Name -eq $name) { return $w } }
  return $null
}
function Find-Button($root, $text) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $text)
  foreach ($m in $root.FindAll($TS::Descendants, $cond)) {
    $cur = $m
    for ($i = 0; $i -lt 8 -and $null -ne $cur; $i++) {
      if ($cur.Current.ControlType.Id -eq $buttonId) { return $cur }
      $cur = $raw.GetParent($cur)
    }
  }
  return $null
}
function Invoke-Button($root, $text, $settle = 700) {
  $b = Find-Button $root $text
  if ($null -eq $b) { throw "no button for '$text'" }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Log "invoked '$text'"
  Start-Sleep -Milliseconds $settle
}
# The first Text element whose Name starts with any of the prefixes.
function Find-Text($root, $prefixes) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  foreach ($t in $root.FindAll($TS::Descendants, $cond)) {
    $n = $t.Current.Name
    foreach ($p in $prefixes) { if ($n.StartsWith($p)) { return $n } }
  }
  return $null
}
function Wait-Text($root, $prefixes, $timeoutSec) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
    $n = Find-Text $root $prefixes
    if ($null -ne $n) { return $n }
    Start-Sleep -Milliseconds 500
  }
  return $null
}

$dash = Get-Window "UnboundKeys"
if ($null -eq $dash) { throw "dashboard not visible" }
Log ("dashboard found, pid " + $proc.Id)

Invoke-Button $dash "Settings"
Invoke-Button $dash "Check for updates"
$confirm = Find-Text $dash @("Check Fizzil")
Log "confirm card: $confirm"
Invoke-Button $dash "Check"
$result = Wait-Text $dash @("You're up to date", "UnboundKeys 4", "Couldn't check") 40
Log "result: $result"
if ($null -eq $result) { throw "no result within 40s" }
if (-not $result.StartsWith("UnboundKeys 4")) { Log "not an update; stopping" }
else {
  $detail = Find-Text $dash @("You have")
  Log "detail: $detail"

  Invoke-Button $dash "Download and install"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $last = ""
  while ($sw.Elapsed.TotalMinutes -lt 12) {
    $old = Get-Process $OldName -ErrorAction SilentlyContinue
    $new = Get-Process $NewName -ErrorAction SilentlyContinue
    if ($null -eq $old) { Log ("old process gone; new running: " + ($null -ne $new)); break }
    try { $m = Find-Text $dash @("Downloading", "Unpacking", "Starting", "Couldn't", "Cancelled") } catch { $m = "(window gone)" }
    if ($m -ne $last) { Log "state: $m"; $last = $m }
    if ($m -like "Couldn't*") { $d = Find-Text $dash @("GitHub", "The ", "That ", "No "); Log "error detail: $d"; break }
    Start-Sleep -Seconds 2
  }
  Start-Sleep -Seconds 3
  Log ("final: old=" + ($null -ne (Get-Process $OldName -ErrorAction SilentlyContinue)) + " new=" + ($null -ne (Get-Process $NewName -ErrorAction SilentlyContinue)))
  $newProc = Get-Process $NewName -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($newProc) { Log ("new exe: " + $newProc.Path) }
}
Log "done"
} catch {
  Log ("ERROR: " + $_.Exception.Message)
  Log $_.ScriptStackTrace
}
Get-Content $log
