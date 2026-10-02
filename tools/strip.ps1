param([string]$Out, [int]$X, [int]$Y, [int]$W, [int]$H, [int]$Frames = 6, [int]$IntervalMs = 120, [string]$Cmd = "", [int]$DelayMs = 0)
# Captures a fixed screen rectangle several times, stacked vertically (good for lineups of figures).
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null
$sheet = New-Object System.Drawing.Bitmap $W, ($H * $Frames)
$gs = [System.Drawing.Graphics]::FromImage($sheet)
if ($Cmd) { Set-Content "$env:TEMP\doodlefolk_cmd.txt" ($Cmd -replace '\|', "`n"); Start-Sleep -Milliseconds (80 + $DelayMs) }
for ($i = 0; $i -lt $Frames; $i++) {
  $bmp = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size $W, $H))
  $gs.DrawImage($bmp, 0, $i * $H)
  $gs.DrawLine([System.Drawing.Pens]::Magenta, 0, $i * $H, $W, $i * $H)
  $g.Dispose(); $bmp.Dispose()
  Start-Sleep -Milliseconds $IntervalMs
}
$sheet.Save($Out); "saved $Out"
