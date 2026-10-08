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
# gets photographed. A dashboard hidden to the tray is asked to show. The
# two windows may overlap: each is brought in front of the other for its
# own pictures. Afterwards the keyboard size, its layout and the open page
# are put back, and the keyboard is hidden again if this script opened it.
# ASCII only: Windows PowerShell reads this file as ANSI.
#
# To retake only some pictures, name them: -Only "Mouse,Settings". The names
# are Mouse, Keyboard-Page, Voice, Editor, Settings, Help, and Keyboard for
# the two pictures of the on-screen keyboard itself.
param(
  [string]$Out = (Join-Path $PSScriptRoot "..\Assets\screenshots"),
  [string]$Method = "sendinput",  # "manual": Mini/Maxi were clicked by hand; capture what shows
  [string]$Only = "",             # comma-separated picture names; empty takes them all
  [string]$EditorChip = "W"       # the Mouse-page chip whose editor is pictured (its text is what the button sends)
)
$wanted = @($Only.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
function Want($name) { return ($wanted.Count -eq 0) -or ($wanted -contains $name) }
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
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string message);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
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

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class T {
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr data);
  public delegate bool EnumProc(IntPtr h, IntPtr data);
  public static IntPtr FindByTitle(string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, d) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString() == title) { found = h; return false; }
      return true; }, IntPtr.Zero);
    return found;
  }
}
"@
# By exact title among the visible top-level windows, then wrapped as an
# automation element. (Filtering the automation root by process id missed
# the dashboard once when two copies of the app were running.)
function Get-Window($name) {
  $h = [T]::FindByTitle($name)
  if ($h -eq [IntPtr]::Zero) { return $null }
  return [System.Windows.Automation.AutomationElement]::FromHandle($h)
}

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

# Any element whose Name is exactly $text, button or not (a row label).
function Find-Text($root, $text) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $text)
  return $root.FindFirst($TS::Descendants, $cond)
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

# Puts a window in front of the app's other window without moving it or
# giving it focus. Both are always-on-top, and the pictures are copied from
# the screen, so whichever is in front where they overlap is what shows:
# the dashboard once covered the right-hand side of the keyboard picture
# (and a covered Mini/Maxi key would take the click meant for it).
function Raise-Window($el) {
  $h = [IntPtr]$el.Current.NativeWindowHandle
  [void][N]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x13)   # topmost; no move, no size, no activate
  Start-Sleep -Milliseconds 300
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

# The dashboard, by title: with the keyboard open, the process's main
# window handle can point at the wrong window.
function Wait-Dashboard {
  for ($i = 0; $i -lt 10; $i++) {
    $w = Get-Window "UnboundKeys"
    if ($null -ne $w) { return $w }
    Start-Sleep -Milliseconds 300
  }
  return $null
}
$dash = Get-Window "UnboundKeys"
if ($null -eq $dash) {
  # Hidden to the tray: ask for it the way a second launch of the app does
  # (see SingleInstance.cs), and give it a moment to appear.
  $show = [N]::RegisterWindowMessage("UnboundKeys.ShowDashboard")
  [void][N]::PostMessage([IntPtr]0xFFFF, $show, [IntPtr]::Zero, [IntPtr]::Zero)
  Log "dashboard was hidden; asked it to show"
  $dash = Wait-Dashboard
}
if ($null -eq $dash) {
  # No answer (up to 4.6.0 a dashboard that had not been shown since the
  # app started did not hear that request): the keyboard's Menu key.
  $strip = Get-Window "UnboundKeys keyboard"
  if ($null -ne $strip) {
    Push-Key $strip "Menu"
    Log "no answer; clicked the keyboard's Menu key"
    $dash = Wait-Dashboard
  }
}
if ($null -eq $dash) { throw "dashboard window not visible (say 'press menu')" }
Log ("dashboard found, pid " + $proc.Id + ", keyboard size " + $originalSize + ", method " + $Method)

# The five pages and an editor. Choosing a rail item closes the editor.
Raise-Window $dash
if (Want "Mouse")         { Invoke-Button $dash "Mouse";    Save-Window $dash "UBK-Mouse.png" }
if (Want "Keyboard-Page") { Invoke-Button $dash "Keyboard"; Save-Window $dash "UBK-Keyboard-Page.png" }
if (Want "Voice")         { Invoke-Button $dash "Voice";    Save-Window $dash "UBK-Voice.png" }
if (Want "Editor") {
  # A mouse button's editor, for its Single press / Double press segments
  # and Reset both (4.10.0): the Mouse-page chip named by -EditorChip, the
  # Middle Button's "W" in Fizzil's play profile. The voice word's editor
  # as before if no such chip is showing.
  Invoke-Button $dash "Mouse"
  if ($null -ne (Find-Button $dash $EditorChip)) {
    Invoke-Button $dash $EditorChip 900
  } else {
    Log "no chip named $EditorChip on the Mouse page; picturing the voice word's editor"
    Invoke-Button $dash "Voice"
    Invoke-Button $dash '"press one"'
  }
  # A plain Tap shows only a few rows; for the picture, Repeat reveals the
  # duration, Infinite and Infinite pause rows, then Tap puts it back.
  $wasTap = ($null -eq (Find-Text $dash "Duration"))
  if ($wasTap) { Invoke-Button $dash "Repeat" 900 }
  Save-Window $dash "UBK-Editor.png"
  if ($wasTap) { Invoke-Button $dash "Tap" 600 }
}
if (Want "Settings")      { Invoke-Button $dash "Settings"; Save-Window $dash "UBK-Settings.png" }
if (Want "Help")          { Invoke-Button $dash "Help";     Save-Window $dash "UBK-Help.png" }

if (Want "Keyboard") {
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
  # From here on the keyboard is not as the user left it, so whatever
  # happens below, the size (and an opened keyboard) is put back in finally.
  try {
    Raise-Window $kb
    $isMini = $null -ne (Find-Button $kb "Maxi")
    if ($isMini) { Save-Window $kb "UBK-Keyboard-Mini.png" 10 } else { Save-Window $kb "UBK-Keyboard.png" 10 }
    if ($Method -eq "sendinput") {
      # A click right after the size change has missed once (the keys are
      # rebuilt at the new size); a second try has always landed.
      if ($isMini) {
        Push-Key $kb "Maxi"
        if (-not (Wait-Button $kb "Mini")) { Push-Key $kb "Maxi" }
        if (-not (Wait-Button $kb "Mini")) { throw "Maxi click did not switch the layout" }
        Raise-Window $kb
        Save-Window $kb "UBK-Keyboard.png" 10
        Push-Key $kb "Mini"
      } else {
        Push-Key $kb "Mini"
        if (-not (Wait-Button $kb "Maxi")) { Push-Key $kb "Mini" }
        if (-not (Wait-Button $kb "Maxi")) { throw "Mini click did not switch the layout" }
        Raise-Window $kb
        Save-Window $kb "UBK-Keyboard-Mini.png" 10
        Push-Key $kb "Maxi"
      }
    }
  } finally {
    Invoke-Button $dash $originalSize 600
    if ($opened) { Invoke-Button $dash "Show on-screen keyboard" 600 }
  }
}
# Leave the dashboard on its first page, in front.
Invoke-Button $dash "Mouse"
Raise-Window $dash
Log "done"
} catch {
  Log ("ERROR: " + $_.Exception.Message)
  Log $_.ScriptStackTrace
}
Get-Content $log
