using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace StickFight;

/// <summary>The StickFight Studio: a borderless window (native shadow, snapping and rounded corners kept)
/// hosting the hand-drawn web UI. The page draws its own title bar; dragging uses CSS app-region and
/// resizing is driven from the page's edges.</summary>
sealed class StudioWindow : Form
{
    const string Host = "https://studio.stickfight/";
    readonly WebView2 _web;
    readonly Action<JsonElement> _onMessage;
    readonly bool _debug;
    bool _ready;
    readonly Queue<string> _pending = new();
    readonly string _page;
    readonly bool _quick, _pop;
    Point _anchor;
    bool _wantShow;

    bool _pageReady;
    public bool PageReady => _pageReady;

    /// <param name="quick">The small tray panel: no taskbar button, sits above the tray, closes when you click away.</param>
    /// <param name="pop">The right-click menu: stays loaded, appears at the cursor sized to its content, hides when you click away.</param>
    public StudioWindow(Action<JsonElement> onMessage, bool debug, bool quick = false, bool pop = false)
    {
        _onMessage = onMessage;
        _debug = debug;
        _quick = quick;
        _pop = pop;
        _page = pop ? "pop.html" : quick ? "quick.html" : "index.html";
        Text = "StickFight Studio";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        float dpi = DeviceDpi / 96f;
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        Size = new Size(Math.Min((int)(1120 * dpi), work.Width - 40), Math.Min((int)(780 * dpi), work.Height - 40));
        MinimumSize = new Size((int)(720 * dpi), (int)(500 * dpi));
        BackColor = System.Drawing.Color.FromArgb(0xF6, 0xF1, 0xE4);
        Icon = App.AppIcon;
        KeyPreview = true;

        if (quick)
        {
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = Size.Empty;
            Size = new Size((int)(340 * dpi), (int)(520 * dpi));
            // Just above the tray, on whichever screen the cursor is.
            var c = Cursor.Position;
            var wa = Screen.FromPoint(c).WorkingArea;
            Location = new Point(Math.Clamp(c.X - Width / 2, wa.Left + 8, wa.Right - Width - 8), Math.Clamp(c.Y - Height - 12, wa.Top + 8, wa.Bottom - Height - 8));
            Deactivate += (_, _) => { if (_pageReady) BeginInvoke(Close); };
        }
        if (pop)
        {
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = Size.Empty;
            Size = new Size((int)(336 * dpi), (int)(260 * dpi));
            Location = new Point(-32000, -32000);   // loads off-screen the first time; moved into place once it knows its size
            Deactivate += (_, _) => { if (_pageReady && Visible) BeginInvoke(Hide); };
        }
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
    }

    async Task InitAsync()
    {
        try
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickFight", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception e)
        {
            World.Log($"Studio: WebView2 unavailable: {e.Message}");
            MessageBox.Show(this, "StickFight Studio needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11.\n\n" + e.Message,
                            "StickFight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }
        var cw = _web.CoreWebView2;
        var s = cw.Settings;
        s.AreDefaultContextMenusEnabled = _debug;
        s.AreDevToolsEnabled = _debug;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = _debug;
        s.IsNonClientRegionSupportEnabled = true;     // CSS app-region: drag on the title bar

        cw.AddWebResourceRequestedFilter(Host + "*", CoreWebView2WebResourceContext.All);
        cw.WebResourceRequested += Serve;
        cw.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var msg = doc.RootElement.Clone();
                if (msg.TryGetProperty("t", out var t)) HandleChrome(t.GetString(), msg);
            }
            catch (Exception ex) { World.Log($"Studio message failed: {ex.Message}"); }
        };
        // Only our own pages load in here; links go to the real browser (and only https ones).
        cw.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(Host, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        cw.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
        _ready = true;
        cw.Navigate(Host + _page);
    }

    static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.ToString()) { UseShellExecute = true });
    }

    void HandleChrome(string? t, JsonElement msg)
    {
        switch (t)
        {
            case "ready":
                _pageReady = true;
                _onMessage(msg);
                while (_pending.Count > 0) Post(_pending.Dequeue());
                return;
            case "min": WindowState = FormWindowState.Minimized; return;
            case "max": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; return;
            case "close": if (_pop) Hide(); else Close(); return;
            case "fit": if (_pop) Fit(msg.GetProperty("h").GetSingle()); return;
            case "resize":
                if (WindowState != FormWindowState.Normal) return;
                int ht = msg.GetProperty("edge").GetString() switch
                {
                    "l" => HTLEFT, "r" => HTRIGHT, "t" => HTTOP, "b" => HTBOTTOM,
                    "tl" => HTTOPLEFT, "tr" => HTTOPRIGHT, "bl" => HTBOTTOMLEFT, _ => HTBOTTOMRIGHT,
                };
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)ht, IntPtr.Zero);
                return;
        }
        _onMessage(msg);
    }

    /// <summary>Right-click menu: show next to this screen point once the page has laid out its content.</summary>
    public void PopAt(Point p)
    {
        _anchor = p;
        _wantShow = true;
    }

    void Fit(float cssHeight)
    {
        float dpi = DeviceDpi / 96f;
        var wa = Screen.FromPoint(_anchor).WorkingArea;
        int h = Math.Min((int)Math.Ceiling(cssHeight * dpi) + 2, wa.Height - 16);
        int x = _anchor.X + (int)(14 * dpi), y = _anchor.Y - (int)(18 * dpi);
        if (x + Width > wa.Right - 8) x = _anchor.X - Width - (int)(14 * dpi);   // flip to the left near the right edge
        x = Math.Clamp(x, wa.Left + 8, wa.Right - Width - 8);
        y = Math.Clamp(y, wa.Top + 8, wa.Bottom - h - 8);
        if (!_wantShow && !Visible) return;
        SetBounds(x, y, Width, h);
        if (_wantShow)
        {
            _wantShow = false;
            if (!Visible) Show();
            Activate();
        }
    }

    /// <summary>Send a JSON message to the page (queued until it has loaded).</summary>
    public void Post(string json)
    {
        if (!_ready || !PageReady) { if (_pending.Count < 50) _pending.Enqueue(json); return; }
        try { _web.CoreWebView2.PostWebMessageAsJson(json); }
        catch (InvalidOperationException) { }
    }

    /// <summary>Debug: run script in the page and log the result.</summary>
    public async void Eval(string js)
    {
        if (!_ready) return;
        string r = await _web.CoreWebView2.ExecuteScriptAsync(js);
        World.Log($"studio eval: {(r.Length > 400 ? r[..400] : r)}");
    }

    // ---------------- serving the embedded UI ----------------

    static readonly Assembly Asm = typeof(StudioWindow).Assembly;

    void Serve(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        string path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        if (path == "") path = "index.html";
        var stream = Asm.GetManifestResourceStream("studio/" + path);
        var env = _web.CoreWebView2.Environment;
        if (stream == null) { e.Response = env.CreateWebResourceResponse(null, 404, "Not found", ""); return; }
        string type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8", ".js" => "text/javascript; charset=utf-8",
            ".svg" => "image/svg+xml", ".png" => "image/png", _ => "application/octet-stream",
        };
        // Copy into memory: WebView2 reads the stream asynchronously and doesn't dispose it.
        var ms = new MemoryStream();
        using (stream) stream.CopyTo(ms);
        ms.Position = 0;
        e.Response = env.CreateWebResourceResponse(ms, 200, "OK", $"Content-Type: {type}\nCache-Control: no-cache");
    }

    // ---------------- custom frame ----------------

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (!_quick && !_pop) cp.Style |= WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Win11 rounded corners, and a 1px frame extension so DWM keeps drawing the shadow.
        int round = 2;
        DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        var m = new MARGINS { Top = 1 };
        DwmExtendFrameIntoClientArea(Handle, ref m);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);   // SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // The whole window is client area (no system title bar or borders)...
            if (WindowState == FormWindowState.Maximized)
            {
                // ...except maximized windows overhang the screen by the frame size; clip to the work area.
                var p = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
                var work = Screen.FromHandle(Handle).WorkingArea;
                p.rgrc0 = new RECT { Left = work.Left, Top = work.Top, Right = work.Right, Bottom = work.Bottom };
                Marshal.StructureToPtr(p, m.LParam, false);
            }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_SIZE) Post($"{{\"t\":\"winstate\",\"max\":{(WindowState == FormWindowState.Maximized ? "true" : "false")}}}");
    }

    const int WM_NCCALCSIZE = 0x0083, WM_NCLBUTTONDOWN = 0x00A1, WM_SIZE = 0x0005;
    const int WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NCCALCSIZE_PARAMS { public RECT rgrc0, rgrc1, rgrc2; public IntPtr lppos; }
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
}
