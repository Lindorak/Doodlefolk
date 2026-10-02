using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>GPU renderer: a premultiplied-alpha DXGI swap chain shown through DirectComposition,
/// so everything not drawn is fully see-through.</summary>
sealed class Renderer : IDisposable
{
    readonly ID3D11Device _d3d;
    readonly IDXGIDevice _dxgi;
    readonly IDXGISwapChain1 _swap;
    readonly IDCompositionDevice _dcomp;
    readonly IDCompositionTarget _target;
    readonly IDCompositionVisual _visual;
    readonly ID2D1Factory1 _factory;
    readonly ID2D1Device _d2d;
    readonly ID2D1DeviceContext _ctx;
    readonly ID2D1SolidColorBrush _brush;
    readonly ID2D1StrokeStyle _round;
    ID2D1Bitmap1? _bitmap;
    Rectangle _bounds;

    public Renderer(IntPtr hwnd, Rectangle bounds)
    {
        _bounds = bounds;
        Vortice.Direct3D.FeatureLevel[] levels =
        {
            Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1, Vortice.Direct3D.FeatureLevel.Level_10_0,
        };
        D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out _d3d!).CheckError();
        _dxgi = _d3d.QueryInterface<IDXGIDevice>();
        using (var d1 = _d3d.QueryInterface<IDXGIDevice1>()) d1.MaximumFrameLatency = 1;

        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        var desc = new SwapChainDescription1
        {
            Width = (uint)bounds.Width,
            Height = (uint)bounds.Height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
        };
        _swap = factory.CreateSwapChainForComposition(_d3d, desc);

        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dxgi);
        _dcomp.CreateTargetForHwnd(hwnd, true, out _target).CheckError();
        _visual = _dcomp.CreateVisual();
        _visual.SetContent(_swap);
        _target.SetRoot(_visual);
        _dcomp.Commit();

        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        _d2d = _factory.CreateDevice(_dxgi);
        _ctx = _d2d.CreateDeviceContext(DeviceContextOptions.None);
        BindTarget();
        _brush = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _round = _factory.CreateStrokeStyle(new StrokeStyleProperties1
        {
            StartCap = CapStyle.Round,
            EndCap = CapStyle.Round,
            LineJoin = LineJoin.Round,
        });
    }

    void BindTarget()
    {
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        var props = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw);
        _bitmap = _ctx.CreateBitmapFromDxgiSurface(surface, props);
        _ctx.Target = _bitmap;
    }

    public void Resize(Rectangle bounds)
    {
        if (bounds == _bounds) return;
        _bounds = bounds;
        _ctx.Target = null;
        _bitmap?.Dispose();
        _swap.ResizeBuffers(2, (uint)bounds.Width, (uint)bounds.Height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        BindTarget();
    }

    /// <summary>Vsync intervals per present; 2 on a 120 Hz display gives ~60 fps.</summary>
    public uint SyncInterval = 1;

    /// <summary>Repaints only the given screen-space regions (cleared, then drawn into) and presents
    /// them as dirty rects, so neither we nor the compositor touch the rest of the screen.</summary>
    public void Frame(List<Rectangle> regions, Action draw) => Frame(regions, _ => draw());

    /// <summary>As above, telling the drawing code which screen rectangle is being repainted, so it can skip
    /// everything that doesn't touch it (the scene is drawn once per region).</summary>
    public void Frame(List<Rectangle> regions, Action<System.Drawing.RectangleF> draw)
    {
        var rects = new List<Vortice.RawRect>(regions.Count);
        foreach (var r in regions)
        {
            var c = Rectangle.Intersect(r, _bounds);
            if (c.Width > 0 && c.Height > 0) rects.Add(new Vortice.RawRect(c.Left - _bounds.X, c.Top - _bounds.Y, c.Right - _bounds.X, c.Bottom - _bounds.Y));
        }
        if (rects.Count == 0) return;

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _ctx.BeginDraw();
        _ctx.Transform = Matrix3x2.Identity;
        foreach (var r in rects)
        {
            _ctx.PushAxisAlignedClip(new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), AntialiasMode.Aliased);
            _ctx.Clear(new Color4(0, 0, 0, 0));
            _ctx.Transform = Matrix3x2.CreateTranslation(-_bounds.X, -_bounds.Y);
            draw(new System.Drawing.RectangleF(r.Left + _bounds.X, r.Top + _bounds.Y, r.Right - r.Left, r.Bottom - r.Top));
            _ctx.Transform = Matrix3x2.Identity;
            _ctx.PopAxisAlignedClip();
        }
        _ctx.EndDraw();
        long tp = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0, tp).TotalMilliseconds;
        Regions += rects.Count;

        _swap.Present1(SyncInterval, PresentFlags.None, new PresentParameters { DirtyRectangles = rects.ToArray() });
    }

    /// <summary>Profiling: time spent drawing (not presenting) and regions drawn, since last reset.</summary>
    public double DrawMs;
    public int Regions;

    public Rectangle Bounds => _bounds;

    public void Line(Vector2 a, Vector2 b, Color4 c, float width)
    {
        _brush.Color = c;
        _ctx.DrawLine(a, b, _brush, width, _round);
    }

    public void Disc(Vector2 center, float r, Color4 c) => Oval(center, r, r, c);

    public void Oval(Vector2 center, float rx, float ry, Color4 c)
    {
        _brush.Color = c;
        _ctx.FillEllipse(new Ellipse(center, rx, ry), _brush);
    }

    public void Ring(Vector2 center, float r, Color4 c, float width)
    {
        _brush.Color = c;
        _ctx.DrawEllipse(new Ellipse(center, r, r), _brush, width);
    }

    // Shapes that never change (an object's parts in its own units) are built once and drawn with a transform.
    readonly Dictionary<(object, int), ID2D1PathGeometry> _shapes = new();

    ID2D1PathGeometry BuildGeometry(ReadOnlySpan<Vector2> pts)
    {
        var geo = _factory.CreatePathGeometry();
        using var sink = geo.Open();
        sink.BeginFigure(pts[0], FigureBegin.Filled);
        sink.AddLines(pts[1..].ToArray());
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        return geo;
    }

    /// <summary>Fill and outline a polygon given in local units, cached under (key, variant); <paramref name="world"/>
    /// places it on screen. <paramref name="strokeLocal"/> is the outline width in local units (0: none).</summary>
    public void CachedShape(object key, int variant, ReadOnlySpan<Vector2> localPts, Matrix3x2 world, Color4 fill, Color4 stroke, float strokeLocal)
    {
        if (localPts.Length < 3) return;
        if (!_shapes.TryGetValue((key, variant), out var geo))
        {
            if (_shapes.Count > 4000) { foreach (var g in _shapes.Values) g.Dispose(); _shapes.Clear(); }
            _shapes[(key, variant)] = geo = BuildGeometry(localPts);
        }
        var old = _ctx.Transform;
        _ctx.Transform = world * old;
        _brush.Color = fill;
        _ctx.FillGeometry(geo, _brush, null);
        if (strokeLocal > 0 && stroke.A > 0) { _brush.Color = stroke; _ctx.DrawGeometry(geo, _brush, strokeLocal, _round); }
        _ctx.Transform = old;
    }

    /// <summary>Fill a polygon and draw its outline in one go (one geometry instead of a line per edge).</summary>
    public void Polygon(ReadOnlySpan<Vector2> pts, Color4 fill, Color4 stroke, float strokeW)
    {
        if (pts.Length < 3) return;
        using var geo = BuildGeometry(pts);
        _brush.Color = fill;
        _ctx.FillGeometry(geo, _brush, null);
        if (strokeW > 0 && stroke.A > 0) { _brush.Color = stroke; _ctx.DrawGeometry(geo, _brush, strokeW, _round); }
    }

    public void FillPolygon(ReadOnlySpan<Vector2> pts, Color4 c)
    {
        if (pts.Length < 3) return;
        using var geo = _factory.CreatePathGeometry();
        using (var sink = geo.Open())
        {
            sink.BeginFigure(pts[0], FigureBegin.Filled);
            sink.AddLines(pts[1..].ToArray());
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }
        _brush.Color = c;
        _ctx.FillGeometry(geo, _brush, null);
    }

    public void RoundRect(Vector2 center, float w, float h, float radius, Color4 fill, Color4 stroke, float strokeW)
    {
        var rr = new RoundedRectangle(new RectangleF(center.X - w / 2, center.Y - h / 2, w, h), radius, radius);
        _brush.Color = fill;
        _ctx.FillRoundedRectangle(rr, _brush);
        _brush.Color = stroke;
        _ctx.DrawRoundedRectangle(rr, _brush, strokeW);
    }

    readonly Dictionary<int, IDWriteTextFormat> _fonts = new();
    readonly Dictionary<(string, int), IDWriteTextLayout> _layouts = new();
    IDWriteFactory? _dwrite;

    /// <summary>Centered bold text; <paramref name="hand"/>: the Studio's handwriting font (Ink Free).</summary>
    public void Text(string text, Vector2 center, float size, Color4 c, bool hand = false)
    {
        int key = (int)MathF.Round(size);
        if (key < 4) return;
        _dwrite ??= DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
        if (!_fonts.TryGetValue(key * 2 + (hand ? 1 : 0), out var fmt))
        {
            fmt = _dwrite.CreateTextFormat(hand ? "Ink Free" : "Segoe UI", null, hand ? FontWeight.Bold : FontWeight.Black, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, key, "en-us");
            fmt.TextAlignment = TextAlignment.Center;
            fmt.ParagraphAlignment = ParagraphAlignment.Center;
            fmt.WordWrapping = WordWrapping.NoWrap;   // centred; longer phrases simply spill wider than the layout box
            _fonts[key * 2 + (hand ? 1 : 0)] = fmt;
        }
        // Laying out text is the slow part; the same few strings ("!", "♪", a name) are drawn again and again.
        var lk = (text, key * 2 + (hand ? 1 : 0));
        if (!_layouts.TryGetValue(lk, out var layout))
        {
            if (_layouts.Count > 300) { foreach (var l in _layouts.Values) l.Dispose(); _layouts.Clear(); }
            _layouts[lk] = layout = _dwrite.CreateTextLayout(text, fmt, key * 8, key * 2);
        }
        _brush.Color = c;
        _ctx.DrawTextLayout(new Vector2(center.X - key * 4, center.Y - key), layout, _brush, DrawTextOptions.None);
    }

    public void Dispose()
    {
        foreach (var f in _fonts.Values) f.Dispose();
        foreach (var l in _layouts.Values) l.Dispose();
        foreach (var g in _shapes.Values) g.Dispose();
        _dwrite?.Dispose();
        _round.Dispose(); _brush.Dispose(); _ctx.Target = null; _bitmap?.Dispose(); _ctx.Dispose(); _d2d.Dispose();
        _factory.Dispose(); _visual.Dispose(); _target.Dispose(); _dcomp.Dispose(); _swap.Dispose(); _dxgi.Dispose(); _d3d.Dispose();
    }
}
