# Test helper: move the real mouse and sample where it is (for testing cursor features while nobody's at the PC).
Add-Type -Namespace SF -Name Mouse -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct POINT { public int X; public int Y; }
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
'@ -ErrorAction SilentlyContinue
function Move-Mouse([int]$x, [int]$y) { [SF.Mouse]::SetCursorPos($x, $y) | Out-Null }
function Get-Mouse { $p = New-Object SF.Mouse+POINT; [SF.Mouse]::GetCursorPos([ref]$p) | Out-Null; "$($p.X),$($p.Y)" }
