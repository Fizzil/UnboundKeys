# Builds Assets/skull.ico from Assets/skull.png: the picture fitted into a
# square with transparent padding, at every size Windows asks for, each
# frame stored as PNG (the Vista+ icon format, which the exe icon, the
# taskbar, System.Drawing.Icon and the tray all accept). Re-run after
# changing the PNG. ASCII only: Windows PowerShell reads this as ANSI.
param(
  [string]$Png = (Join-Path $PSScriptRoot "../Assets/skull.png"),
  [string]$Ico = (Join-Path $PSScriptRoot "../Assets/skull.ico")
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$Png = [IO.Path]::GetFullPath($Png)
$Ico = [IO.Path]::GetFullPath($Ico)
$src = [System.Drawing.Image]::FromFile($Png)
$sizes = 256, 64, 48, 40, 32, 24, 20, 16
$frames = foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $scale = [Math]::Min($s / $src.Width, $s / $src.Height)
  $w = [int][Math]::Round($src.Width * $scale); $h = [int][Math]::Round($src.Height * $scale)
  $x = [int][Math]::Floor(($s - $w) / 2); $y = [int][Math]::Floor(($s - $h) / 2)
  $g.DrawImage($src, (New-Object System.Drawing.Rectangle($x, $y, $w, $h)))
  $g.Dispose()
  $ms = New-Object IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  @{ Size = $s; Bytes = $ms.ToArray() }
}
$src.Dispose()
$out = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter($out)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
  $dim = 0; if ($f.Size -lt 256) { $dim = $f.Size }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$f.Bytes.Length); $bw.Write([uint32]$offset)
  $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.Bytes) }
$bw.Flush()
[IO.File]::WriteAllBytes($Ico, $out.ToArray())
"wrote $Ico with " + $frames.Count + " sizes, " + $out.Length + " bytes"
