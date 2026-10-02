param([string]$Title, [string]$Out)
# Screenshot one of Doodlefolk's own windows (e.g. an editor) by its title.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class Win {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Win]::SetProcessDPIAware() | Out-Null
$sfPid = (Get-Process Doodlefolk).Id
$script:found = [IntPtr]::Zero
[Win]::EnumWindows({ param($h, $l)
  [uint32]$p = 0; [Win]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
  $sb = New-Object Text.StringBuilder 256; [Win]::GetWindowText($h, $sb, 256) | Out-Null
  if ($p -eq $sfPid -and [Win]::IsWindowVisible($h) -and $sb.ToString() -eq $Title) { $script:found = $h; return $false }
  return $true }, [IntPtr]::Zero) | Out-Null
if ($script:found -eq [IntPtr]::Zero) { "window '$Title' not found"; exit 1 }
$r = New-Object Win+RECT; [Win]::DwmGetWindowAttribute($script:found, 9, [ref]$r, 16) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save($Out); "saved $Out ($w x $h)"
