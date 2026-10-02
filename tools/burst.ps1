param([string]$Out, [string]$Name = "", [int]$Frames = 8, [int]$IntervalMs = 70, [int]$W = 360, [int]$H = 300, [string]$Cmd = "", [int]$DelayMs = 0)
# Captures a burst of frames around a figure into one contact sheet (debug builds write the state file).
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null
$sheet = New-Object System.Drawing.Bitmap ($W * [Math]::Min($Frames, 4)), ($H * [Math]::Ceiling($Frames / 4))
$gs = [System.Drawing.Graphics]::FromImage($sheet)
# Optional debug command, sent once everything is loaded so frame 0 lines up with it.
if ($Cmd) { Set-Content "$env:TEMP\stickfight_cmd.txt" ($Cmd -replace '\|', "`n"); Start-Sleep -Milliseconds (80 + $DelayMs) }
for ($i = 0; $i -lt $Frames; $i++) {
  $s = Get-Content "$env:TEMP\stickfight_state.json" -Raw | ConvertFrom-Json
  $f = if ($Name) { $s.figures | Where-Object Name -eq $Name | Select-Object -First 1 } else { $s.figures[0] }
  $cx = ($f.bbox[0] + $f.bbox[2]) / 2; $cy = ($f.bbox[1] + $f.bbox[3]) / 2
  $x = [int]($cx - $W / 2); $y = [int]($cy - $H / 2)
  $bmp = New-Object System.Drawing.Bitmap $W, $H
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $W, $H))
  $gs.DrawImage($bmp, ($i % 4) * $W, [Math]::Floor($i / 4) * $H)
  $gs.DrawString("$i $($f.brain)/$($f.action)/$($f.mode)", (New-Object System.Drawing.Font "Consolas", 9), [System.Drawing.Brushes]::Magenta, ($i % 4) * $W + 2, [Math]::Floor($i / 4) * $H + 2)
  $g.Dispose(); $bmp.Dispose()
  Start-Sleep -Milliseconds $IntervalMs
}
$sheet.Save($Out); "saved $Out"
