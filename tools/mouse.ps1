# Test helper: move the real mouse and sample where it is (for testing cursor features while nobody's at the PC).
Add-Type -Namespace SF -Name Mouse -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct POINT { public int X; public int Y; }
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
'@ -ErrorAction SilentlyContinue
function Move-Mouse([int]$x, [int]$y) { [SF.Mouse]::SetCursorPos($x, $y) | Out-Null }
function Get-Mouse { $p = New-Object SF.Mouse+POINT; [SF.Mouse]::GetCursorPos([ref]$p) | Out-Null; "$($p.X),$($p.Y)" }
Add-Type -Namespace SF -Name Click -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(int flags, int dx, int dy, int data, int extra);
'@ -ErrorAction SilentlyContinue
function Click-Mouse([int]$x, [int]$y) { Move-Mouse $x $y; Start-Sleep -Milliseconds 60; [SF.Click]::mouse_event(2, 0, 0, 0, 0); Start-Sleep -Milliseconds 40; [SF.Click]::mouse_event(4, 0, 0, 0, 0) }
