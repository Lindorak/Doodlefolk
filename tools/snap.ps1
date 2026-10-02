param([string]$Out, [int]$Pad = 120)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System.Runtime.InteropServices;
public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[Dpi]::SetProcessDPIAware() | Out-Null
$s = Get-Content "$env:TEMP\doodlefolk_state.json" | ConvertFrom-Json
$i = 0
foreach ($f in $s.figures) {
  $b = $f.bbox
  $x = [int]($b[0] - $Pad); $y = [int]($b[1] - $Pad); $w = [int]($b[2]-$b[0] + 2*$Pad); $h = [int]($b[3]-$b[1] + 2*$Pad)
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h), [System.Drawing.CopyPixelOperation]::SourceCopy)
  $bmp.Save("$Out-$i.png"); $g.Dispose(); $bmp.Dispose()
  "$($f.Name) $($f.mode) $($f.brain) $($f.action) -> $Out-$i.png"
  $i++
}
