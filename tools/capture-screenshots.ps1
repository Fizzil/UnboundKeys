# Takes the README screenshots from the running app, without anyone at the
# mouse: the dashboard is driven through UI Automation (rail buttons,
# tiles and switches are invoked the way a screen reader would), each page
# is captured from the screen once it has settled, and the on-screen
# keyboard is captured at Large in both layouts. The keyboard's keys act on
# real mouse down/up, so Mini and Maxi get a genuine click: the cursor
# jumps to the key and straight back. Keep hands off the mouse while it
# runs (about 20 seconds).
#
# The app runs elevated, so this must too. From a normal PowerShell:
#   Start-Process powershell -Verb RunAs -ArgumentList '-ExecutionPolicy','Bypass','-File','tools\capture-screenshots.ps1'
#
# Fade should be off. Whatever profile and mappings are active are what
# gets photographed. Afterwards the keyboard size, its layout and the
# open page are put back, and the keyboard is hidden again if this script
# opened it. ASCII only: Windows PowerShell reads this file as ANSI.
param(
  [string]$Out = (Join-Path $PSScriptRoot "..\Assets\screenshots"),
  [string]$Method = "sendinput"   # "manual": Mini/Maxi were clicked by hand; capture what shows
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$log = Join-Path $env:TEMP "unboundkeys-capture.log"
"" | Set-Content $log
function Log($m) { Add-Content -Path $log -Value ("{0:HH:mm:ss} {1}" -f (Get-Date), $m) }

try {
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class N {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
[void][N]::SetProcessDPIAware()

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$raw = [System.Windows.Automation.TreeWalker]::RawViewWalker
$buttonId = [System.Windows.Automation.ControlType]::Button.Id

$proc = Get-Process "UnboundKeys-v*" | Select-Object -First 1
if ($null -eq $proc) { throw "UnboundKeys is not running" }

# The keyboard size to put back afterwards (Settings.KeyboardScale).
$settings = Get-Content "$env:APPDATA\UnboundKeys\settings.json" -Raw | ConvertFrom-Json
$originalSize = "Small"
if ($settings.KeyboardScale -ge 0.99) { $originalSize = "Large" } elseif ($settings.KeyboardScale -ge 0.79) { $originalSize = "Medium" }

function Get-Window($name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
  foreach ($w in $AE::RootElement.FindAll($TS::Children, $cond)) { if ($w.Current.Name -eq $name) { return $w } }
  return $null
}

# By title: with the keyboard open, the process's main window handle can
# point at the wrong window.
$dash = Get-Window "UnboundKeys"
if ($null -eq $dash) { throw "dashboard window not visible (say 'press menu')" }
Log ("dashboard found, pid " + $proc.Id + ", keyboard size " + $originalSize + ", method " + $Method)

# Every element whose Name is exactly $text, then the first one that sits
# inside a Button (walking up the raw tree). Skips e.g. the page title.
function Find-Button($root, $text) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $text)
  $found = $root.FindAll($TS::Descendants, $cond)
  foreach ($m in $found) {
    $cur = $m
    for ($i = 0; $i -lt 8 -and $null -ne $cur; $i++) {
      if ($cur.Current.ControlType.Id -eq $buttonId) { return $cur }
      $cur = $raw.GetParent($cur)
    }
  }
  return $null
}

# The layout rebuild after Mini/Maxi takes a moment to reach the
# automation tree, so poll for the key that proves it happened.
function Wait-Button($root, $text, $timeoutMs = 5000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
    if ($null -ne (Find-Button $root $text)) { return $true }
    Start-Sleep -Milliseconds 300
  }
  return $false
}

# Buttons wired to Click (rail, switches, tiles): the Invoke pattern.
function Invoke-Button($root, $text, $settle = 700) {
  $b = Find-Button $root $text
  if ($null -eq $b) { throw "no button for '$text'" }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Log "invoked '$text'"
  Start-Sleep -Milliseconds $settle
}

# The on-screen keyboard's keys act on real mouse down/up: a genuine click
# at the key's centre, with the cursor put back afterwards.
function Push-Key($win, $text, $settle = 900) {
  $b = Find-Button $win $text
  if ($null -eq $b) { throw "no key for '$text'" }
  $r = $b.Current.BoundingRectangle
  $cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
  $save = New-Object N+POINT
  [void][N]::GetCursorPos([ref]$save)
  [void][N]::SetCursorPos($cx, $cy); Start-Sleep -Milliseconds 80
  [N]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  [N]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  [void][N]::SetCursorPos($save.X, $save.Y)
  Log "clicked key '$text'"
  Start-Sleep -Milliseconds $settle
}

# Copies the window's rectangle from the screen. $radius clears the
# corners of a rounded window so the PNG shows nothing behind them.
function Save-Window($el, $file, $radius = 0) {
  $h = [IntPtr]$el.Current.NativeWindowHandle
  $r = New-Object N+RECT
  [void][N]::GetWindowRect($h, [ref]$r)
  $w = $r.R - $r.L; $ht = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap($w, $ht, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
  $g.Dispose()
  if ($radius -gt 0) {
    $clear = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
    for ($y = 0; $y -lt $radius; $y++) {
      for ($x = 0; $x -lt $radius; $x++) {
        $dx = ($x + 0.5) - $radius; $dy = ($y + 0.5) - $radius
        if ($dx * $dx + $dy * $dy -gt $radius * $radius) {
          $bmp.SetPixel($x, $y, $clear)
          $bmp.SetPixel($w - 1 - $x, $y, $clear)
          $bmp.SetPixel($x, $ht - 1 - $y, $clear)
          $bmp.SetPixel($w - 1 - $x, $ht - 1 - $y, $clear)
        }
      }
    }
  }
  $bmp.Save((Join-Path $Out $file), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Log "captured $file ${w}x${ht}"
}

# The five pages and an editor. Choosing a rail item closes the editor.
Invoke-Button $dash "Mouse";    Save-Window $dash "UBK-Mouse.png"
Invoke-Button $dash "Keyboard"; Save-Window $dash "UBK-Keyboard-Page.png"
Invoke-Button $dash "Voice";    Save-Window $dash "UBK-Voice.png"
Invoke-Button $dash '"press one"'; Save-Window $dash "UBK-Editor.png"
Invoke-Button $dash "Settings"; Save-Window $dash "UBK-Settings.png"
Invoke-Button $dash "Help";     Save-Window $dash "UBK-Help.png"

# The on-screen keyboard, at Large for a clear picture. Its window has
# 8-DIP rounded corners; 10 px covers them at 125% scaling.
Invoke-Button $dash "Keyboard"
$kb = Get-Window "UnboundKeys keyboard"
$opened = $false
if ($null -eq $kb) {
  Invoke-Button $dash "Show on-screen keyboard" 1200
  $kb = Get-Window "UnboundKeys keyboard"
  $opened = $true
}
if ($null -eq $kb) { throw "keyboard window not found" }
Invoke-Button $dash "Large" 900
$isMini = $null -ne (Find-Button $kb "Maxi")
if ($isMini) { Save-Window $kb "UBK-Keyboard-Mini.png" 10 } else { Save-Window $kb "UBK-Keyboard.png" 10 }
if ($Method -eq "sendinput") {
  if ($isMini) {
    Push-Key $kb "Maxi"
    if (-not (Wait-Button $kb "Mini")) { throw "Maxi click did not switch the layout" }
    Save-Window $kb "UBK-Keyboard.png" 10
    Push-Key $kb "Mini"
  } else {
    Push-Key $kb "Mini"
    if (-not (Wait-Button $kb "Maxi")) { throw "Mini click did not switch the layout" }
    Save-Window $kb "UBK-Keyboard-Mini.png" 10
    Push-Key $kb "Maxi"
  }
}
Invoke-Button $dash $originalSize 600
if ($opened) { Invoke-Button $dash "Show on-screen keyboard" 600 }
Invoke-Button $dash "Mouse"
Log "done"
} catch {
  Log ("ERROR: " + $_.Exception.Message)
  Log $_.ScriptStackTrace
}
Get-Content $log
