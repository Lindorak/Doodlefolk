using static Doodlefolk.Native;

namespace Doodlefolk;

/// <summary>Borderless, topmost, non-activating window covering every monitor. Click-through
/// everywhere except while the cursor is over a figure.</summary>
sealed class Overlay : Form
{
    bool _clickThrough = true;

    public Overlay(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        Text = "Doodlefolk";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST |
                                WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP);
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
        Place(Bounds);
    }

    public void Place(Rectangle r) =>
        SetWindowPos(Handle, HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, SWP_NOACTIVATE);

    public void KeepOnTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public void SetClickThrough(bool on)
    {
        if (on == _clickThrough) return;
        _clickThrough = on;
        long ex = ExStyle(Handle);
        ex = on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, (IntPtr)ex);
        Cursor = on ? Cursors.Default : _override ?? Cursors.Hand;
    }

    Cursor? _override;
    /// <summary>A particular cursor while over something (a resize edge), or null for the usual hand.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Cursor? CursorOverride
    {
        set { if (_override == value) return; _override = value; if (!_clickThrough) Cursor = value ?? Cursors.Hand; }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = MA_NOACTIVATE; return; }
        base.WndProc(ref m);
    }
}
