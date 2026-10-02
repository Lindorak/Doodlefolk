using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Doodlefolk;

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
        // No usable graphics card (a virtual machine, remote desktop, a build server): Windows' software renderer.
        if (ForceWarp || D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out _d3d!).Failure)
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out _d3d!).CheckError();
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

    /// <summary>Debug: draw something into an offscreen picture and save it as a PNG (nothing appears on screen).</summary>
    public void Snapshot(System.Drawing.RectangleF area, Action<System.Drawing.RectangleF> draw, string path, Color4 background, float zoom = 1)
    {
        uint w = (uint)Math.Max(1, area.Width * zoom), h = (uint)Math.Max(1, area.Height * zoom);
        var fmt = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
        using var target = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI((int)w, (int)h), IntPtr.Zero, 0, new BitmapProperties1(fmt, 96, 96, BitmapOptions.Target));
        using var cpu = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI((int)w, (int)h), IntPtr.Zero, 0, new BitmapProperties1(fmt, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        var old = _ctx.Target;
        _ctx.Target = target;
        _ctx.BeginDraw();
        _ctx.Clear(background);
        _ctx.Transform = Matrix3x2.CreateTranslation(-area.X, -area.Y) * Matrix3x2.CreateScale(zoom);
        draw(area);
        _ctx.Transform = Matrix3x2.Identity;
        _ctx.EndDraw();
        _ctx.Target = old;
        cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read);
        try
        {
            using var bmp = new System.Drawing.Bitmap((int)w, (int)h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, (int)w, (int)h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            for (int y = 0; y < h; y++)
                unsafe { Buffer.MemoryCopy((byte*)map.Bits + y * map.Pitch, (byte*)data.Scan0 + y * data.Stride, data.Stride, w * 4); }
            bmp.UnlockBits(data);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally { cpu.Unmap(); }
    }

    ID2D1Bitmap1? _capTarget, _capCpu;
    int _capW, _capH;

    /// <summary>Draw something offscreen and copy the pixels out (BGRA, tightly packed) for recording a clip.</summary>
    public void Capture(System.Drawing.RectangleF area, Action<System.Drawing.RectangleF> draw, Color4 background, float zoom, byte[] dst, int w, int h)
    {
        var fmt = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
        if (_capTarget == null || _capW != w || _capH != h)
        {
            _capTarget?.Dispose(); _capCpu?.Dispose();
            _capTarget = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI(w, h), IntPtr.Zero, 0, new BitmapProperties1(fmt, 96, 96, BitmapOptions.Target));
            _capCpu = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI(w, h), IntPtr.Zero, 0, new BitmapProperties1(fmt, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
            _capW = w; _capH = h;
        }
        var old = _ctx.Target;
        _ctx.Target = _capTarget;
        _ctx.BeginDraw();
        _ctx.Clear(background);
        _ctx.Transform = Matrix3x2.CreateTranslation(-area.X, -area.Y) * Matrix3x2.CreateScale(zoom);
        draw(area);
        _ctx.Transform = Matrix3x2.Identity;
        _ctx.EndDraw();
        _ctx.Target = old;
        _capCpu!.CopyFromBitmap(_capTarget);
        var map = _capCpu.Map(MapOptions.Read);
        try
        {
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(map.Bits + y * (int)map.Pitch, dst, y * w * 4, w * 4);
        }
        finally { _capCpu.Unmap(); }
    }

    public void EndCapture() { _capTarget?.Dispose(); _capCpu?.Dispose(); _capTarget = _capCpu = null; }

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
        EnsureSprites();
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

        if (!Headless) _swap.Present1(SyncInterval, PresentFlags.None, new PresentParameters { DirtyRectangles = rects.ToArray() });
        else _d3d.ImmediateContext.Flush();   // nothing presents, so make sure the queued work runs (the software renderer never would)
    }

    /// <summary>Draw but never show (the self-test): Windows throttles presents to a hidden window to a few a second,
    /// which would starve the sped-up simulation.</summary>
    public bool Headless;
    /// <summary>Software rendering only (--warp: what a machine without a GPU, like CI, gets).</summary>
    public static bool ForceWarp;

    /// <summary>Profiling: time spent drawing (not presenting) and regions drawn, since last reset.</summary>
    public double DrawMs;
    public int Regions;

    public Rectangle Bounds => _bounds;

    /// <summary>Drop shadows are drawn solid inside one translucent layer, so where parts overlap (joints, a cushion on
    /// a couch, two figures) the shadow stays one even shade instead of doubling up.</summary>
    public void BeginShadowLayer(float opacity) =>
        _ctx.PushLayer(new LayerParameters1 { ContentBounds = new Rect(-100000, -100000, 200000, 200000), MaskTransform = Matrix3x2.Identity, Opacity = opacity, MaskAntialiasMode = AntialiasMode.PerPrimitive }, null);

    public void EndShadowLayer() => _ctx.PopLayer();

    /// <summary>Draw only above this line (a shadow cast on the window behind stops at the surface its owner stands on).</summary>
    public void PushAbove(float y) => _ctx.PushAxisAlignedClip(new Rect(-1e5f, -1e5f, 2e5f, y + 1e5f), AntialiasMode.Aliased);
    public void PopClip() => _ctx.PopAxisAlignedClip();

    public void Line(Vector2 a, Vector2 b, Color4 c, float width)
    {
        _brush.Color = c;
        _ctx.DrawLine(a, b, _brush, width, _round);
    }

    public void Disc(Vector2 center, float r, Color4 c) => Oval(center, r, r, c);

    // ---------------- pictures ----------------

    readonly Dictionary<string, (ID2D1Bitmap1? bmp, int w, int h)> _pictures = new();

    /// <summary>A picture file (a meme in a frame), loaded once (at most 512 px), drawn into a rectangle keeping its
    /// shape (letterboxed). Unreadable files draw nothing.</summary>
    public void Picture(string path, System.Drawing.RectangleF dest, float alpha = 1)
    {
        if (!_pictures.TryGetValue(path, out var p))
        {
            p = (null, 0, 0);
            try
            {
                using var src = new System.Drawing.Bitmap(path);
                float k = Math.Min(1, 512f / Math.Max(src.Width, src.Height));
                using var bmp = new System.Drawing.Bitmap(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k)), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = System.Drawing.Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, bmp.Width, bmp.Height); }
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    var fmt = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
                    p = (_ctx.CreateBitmap(new SizeI(bmp.Width, bmp.Height), data.Scan0, (uint)data.Stride, new BitmapProperties1(fmt, 96, 96, BitmapOptions.None)), bmp.Width, bmp.Height);
                }
                finally { bmp.UnlockBits(data); }
            }
            catch (Exception e) { World.Log($"picture {Path.GetFileName(path)}: {e.Message}"); }
            if (_pictures.Count > 24) { foreach (var old in _pictures.Values) old.bmp?.Dispose(); _pictures.Clear(); }
            _pictures[path] = p;
        }
        if (p.bmp == null || alpha <= 0.003f) return;
        float s = Math.Min(dest.Width / p.w, dest.Height / p.h), w = p.w * s, h = p.h * s;
        float x = dest.X + (dest.Width - w) / 2, y = dest.Y + (dest.Height - h) / 2;
        _ctx.DrawBitmap(p.bmp, new Vortice.RawRectF(x, y, x + w, y + h), Math.Clamp(alpha, 0, 1), Vortice.Direct2D1.InterpolationMode.Linear, null, null);
    }

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
    readonly Dictionary<(object, int), (ID2D1PathGeometry geo, Vector2 min, Vector2 max)> _shapes = new();
    ID2D1LinearGradientBrush? _shadeLin;
    ID2D1RadialGradientBrush? _shadeRad, _soft;

    ID2D1Bitmap1? _softSprite, _glowSprite;

    /// <summary>Soft round gradients are drawn once into small pictures and stretched into place: far cheaper than
    /// filling a gradient every time.</summary>
    // Cached layers of things sitting still, kept as 256-pixel tiles only where there's something to cache (a whole
    // screen's worth of picture per layer was most of the app's memory).
    const int TileSize = 256;
    readonly Dictionary<long, ID2D1Bitmap1> _tilesUnder = new(), _tilesOver = new();
    static long TileKey(int tx, int ty) => ((long)tx << 32) | (uint)ty;
    public int TileCount => _tilesUnder.Count + _tilesOver.Count;

    /// <summary>Redraw the cached layers of things sitting still (see App.Layers): <paramref name="under"/> and
    /// <paramref name="over"/> draw whatever falls in the tile they're given, and the areas say where anything is.
    /// Must be called outside a frame.</summary>
    public void BuildLayers(Action<System.Drawing.RectangleF> under, IReadOnlyList<System.Drawing.RectangleF> underAreas,
                            Action<System.Drawing.RectangleF> over, IReadOnlyList<System.Drawing.RectangleF> overAreas)
    {
        EnsureSprites();
        BuildTiles(_tilesUnder, under, underAreas);
        BuildTiles(_tilesOver, over, overAreas);
    }

    void BuildTiles(Dictionary<long, ID2D1Bitmap1> tiles, Action<System.Drawing.RectangleF> draw, IReadOnlyList<System.Drawing.RectangleF> areas)
    {
        var need = new HashSet<long>();
        foreach (var a0 in areas)
        {
            var a = System.Drawing.RectangleF.Intersect(a0, new System.Drawing.RectangleF(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height));
            if (a.Width <= 0 || a.Height <= 0) continue;
            int tx0 = (int)MathF.Floor(a.Left / TileSize), tx1 = (int)MathF.Floor((a.Right - 0.01f) / TileSize);
            int ty0 = (int)MathF.Floor(a.Top / TileSize), ty1 = (int)MathF.Floor((a.Bottom - 0.01f) / TileSize);
            for (int tx = tx0; tx <= tx1; tx++)
                for (int ty = ty0; ty <= ty1; ty++) need.Add(TileKey(tx, ty));
        }
        foreach (var k in tiles.Keys.Where(k => !need.Contains(k)).ToList()) { tiles[k].Dispose(); tiles.Remove(k); }
        var fmt = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
        var old = _ctx.Target;
        foreach (var k in need)
        {
            if (!tiles.TryGetValue(k, out var bmp))
                tiles[k] = bmp = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI(TileSize, TileSize), IntPtr.Zero, 0, new BitmapProperties1(fmt, 96, 96, BitmapOptions.Target));
            int tx = (int)(k >> 32), ty = (int)(k & 0xFFFFFFFF);
            _ctx.Target = bmp;
            _ctx.BeginDraw();
            _ctx.Clear(new Color4(0, 0, 0, 0));
            _ctx.Transform = Matrix3x2.CreateTranslation(-tx * TileSize, -ty * TileSize);
            draw(new System.Drawing.RectangleF(tx * TileSize, ty * TileSize, TileSize, TileSize));
            _ctx.Transform = Matrix3x2.Identity;
            _ctx.EndDraw();
        }
        _ctx.Target = old;
    }

    public void BlitLayer(bool over, System.Drawing.RectangleF clip)
    {
        var tiles = over ? _tilesOver : _tilesUnder;
        if (tiles.Count == 0) return;
        int tx0 = (int)MathF.Floor(clip.Left / TileSize), tx1 = (int)MathF.Floor((clip.Right - 0.01f) / TileSize);
        int ty0 = (int)MathF.Floor(clip.Top / TileSize), ty1 = (int)MathF.Floor((clip.Bottom - 0.01f) / TileSize);
        for (int tx = tx0; tx <= tx1; tx++)
            for (int ty = ty0; ty <= ty1; ty++)
            {
                if (!tiles.TryGetValue(TileKey(tx, ty), out var bmp)) continue;
                float x0 = MathF.Max(clip.Left, tx * TileSize), y0 = MathF.Max(clip.Top, ty * TileSize);
                float x1 = MathF.Min(clip.Right, (tx + 1) * TileSize), y1 = MathF.Min(clip.Bottom, (ty + 1) * TileSize);
                if (x1 <= x0 || y1 <= y0) continue;
                _ctx.DrawBitmap(bmp, new Vortice.RawRectF(x0, y0, x1, y1), 1, Vortice.Direct2D1.InterpolationMode.NearestNeighbor,
                                new Vortice.RawRectF(x0 - tx * TileSize, y0 - ty * TileSize, x1 - tx * TileSize, y1 - ty * TileSize), null);
            }
    }

    void EnsureSprites()
    {
        if (_softSprite != null) return;
        _softSprite = Sprite(128, 64, new[]
        {
            new GradientStop(0, new Color4(0, 0, 0, 1)), new GradientStop(0.3f, new Color4(0, 0, 0, 0.82f)),
            new GradientStop(0.6f, new Color4(0, 0, 0, 0.4f)), new GradientStop(0.82f, new Color4(0, 0, 0, 0.12f)), new GradientStop(1, new Color4(0, 0, 0, 0)),
        });
        _glowSprite = Sprite(128, 128, new[]
        {
            new GradientStop(0, new Color4(1, 0.82f, 0.45f, 0.9f)), new GradientStop(0.25f, new Color4(1, 0.6f, 0.2f, 0.55f)),
            new GradientStop(0.6f, new Color4(1, 0.42f, 0.1f, 0.18f)), new GradientStop(1, new Color4(1, 0.35f, 0.05f, 0)),
        });
    }

    ID2D1Bitmap1 Sprite(int w, int h, GradientStop[] stops)
    {
        var fmt = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
        var bmp = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI(w, h), IntPtr.Zero, 0, new BitmapProperties1(fmt, 96, 96, BitmapOptions.Target));
        var old = _ctx.Target;
        _ctx.Target = bmp;
        _ctx.BeginDraw();
        _ctx.Clear(new Color4(0, 0, 0, 0));
        using (var coll = _ctx.CreateGradientStopCollection(stops))
        using (var brush = _ctx.CreateRadialGradientBrush(new RadialGradientBrushProperties(new Vector2(w / 2f, h / 2f), Vector2.Zero, w / 2f, h / 2f), coll))
            _ctx.FillEllipse(new Ellipse(new Vector2(w / 2f, h / 2f), w / 2f, h / 2f), brush);
        _ctx.EndDraw();
        _ctx.Target = old;
        return bmp;
    }

    void Blit(ID2D1Bitmap1? sprite, Vector2 c, float rx, float ry, float alpha)
    {
        if (sprite == null || alpha <= 0.003f) return;
        _ctx.DrawBitmap(sprite, new Vortice.RawRectF(c.X - rx, c.Y - ry, c.X + rx, c.Y + ry), Math.Clamp(alpha, 0, 1), Vortice.Direct2D1.InterpolationMode.Linear, null, null);
    }

    /// <summary>Warm firelight: a smooth radial glow, hot in the middle and fading to nothing.</summary>
    public void FireGlow(Vector2 c, float rx, float ry, float alpha) => Blit(_glowSprite, c, rx, ry, alpha);

    /// <summary>A soft shadow: one smooth radial falloff (no rings), darkest in the middle.</summary>
    public void SoftShadow(Vector2 c, float rx, float ry, float alpha) => Blit(_softSprite, c, rx, ry, alpha);

    void EnsureShading()
    {
        if (_shadeLin != null) return;
        using var lin = _ctx.CreateGradientStopCollection(new[]
        {
            new GradientStop(0, new Color4(1, 1, 1, 0.24f)), new GradientStop(0.45f, new Color4(1, 1, 1, 0)),
            new GradientStop(0.6f, new Color4(0, 0, 0, 0)), new GradientStop(1, new Color4(0, 0, 0, 0.2f)),
        });
        _shadeLin = _ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties(Vector2.Zero, Vector2.One), lin);
        using var rad = _ctx.CreateGradientStopCollection(new[]
        {
            new GradientStop(0, new Color4(1, 1, 1, 0.42f)), new GradientStop(0.45f, new Color4(1, 1, 1, 0)),
            new GradientStop(0.72f, new Color4(0, 0, 0, 0)), new GradientStop(1, new Color4(0, 0, 0, 0.26f)),
        });
        _shadeRad = _ctx.CreateRadialGradientBrush(new RadialGradientBrushProperties(Vector2.Zero, Vector2.Zero, 1, 1), rad);
    }

    /// <summary>A disc with a highlight toward the top left and a darker rim (plain disc when shading is off).</summary>
    public void ShadedDisc(Vector2 c, float r, Color4 col)
    {
        Disc(c, r, col);
        ShadeDisc(c, r, col.A);
    }

    /// <summary>Just the light and shade over something round that's already drawn (balls, heads).</summary>
    public void ShadeDisc(Vector2 c, float r, float alpha = 1)
    {
        var col = new Color4(1, 1, 1, alpha);
        if (!Gfx.Q.Shading || r < 1.5f) return;
        EnsureShading();
        _shadeRad!.Center = c;
        _shadeRad.RadiusX = _shadeRad.RadiusY = r;
        _shadeRad.GradientOriginOffset = new Vector2(-r * 0.45f, -r * 0.5f);
        _shadeRad.Opacity = col.A;
        _ctx.FillEllipse(new Ellipse(c, r, r), _shadeRad);
    }

    /// <summary>A thick line with a thin highlight along its lit side (plain line when shading is off).</summary>
    public void ShadedLine(Vector2 a, Vector2 b, Color4 c, float width)
    {
        Line(a, b, c, width);
        if (!Gfx.Q.Shading || width < 2) return;
        Vector2 d = b - a;
        if (d.LengthSquared() < 1) return;
        Vector2 n = Vector2.Normalize(new Vector2(-d.Y, d.X));
        if (n.X + n.Y > 0) n = -n;   // the side facing the top left
        Vector2 o = n * width * 0.22f;
        Line(a + o, b + o, Gfx.Lighter(c, 0.45f).A(0.55f), width * 0.3f);
    }

    static (Vector2, Vector2) PointBounds(ReadOnlySpan<Vector2> pts)
    {
        Vector2 mn = pts[0], mx = pts[0];
        foreach (var p in pts) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
        return (mn, mx);
    }

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
    public void CachedShape(object key, int variant, ReadOnlySpan<Vector2> localPts, Matrix3x2 world, Color4 fill, Color4 stroke, float strokeLocal, bool shade = false)
    {
        if (localPts.Length < 3) return;
        if (!_shapes.TryGetValue((key, variant), out var entry))
        {
            if (_shapes.Count > 4000) { foreach (var g in _shapes.Values) g.geo.Dispose(); _shapes.Clear(); }
            var (mn, mx) = PointBounds(localPts);
            _shapes[(key, variant)] = entry = (BuildGeometry(localPts), mn, mx);
        }
        var old = _ctx.Transform;
        _ctx.Transform = world * old;
        _brush.Color = fill;
        _ctx.FillGeometry(entry.geo, _brush, null);
        if (shade && Gfx.Q.Shading)
        {
            // Light from the top left of the shape (local y points up), shade toward the bottom right.
            EnsureShading();
            _shadeLin!.StartPoint = new Vector2(entry.min.X, entry.max.Y);
            _shadeLin.EndPoint = new Vector2(entry.max.X, entry.min.Y);
            _shadeLin.Opacity = fill.A;
            _ctx.FillGeometry(entry.geo, _shadeLin, null);
        }
        if (strokeLocal > 0 && stroke.A > 0) { _brush.Color = stroke; _ctx.DrawGeometry(entry.geo, _brush, strokeLocal, _round); }
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
        foreach (var p in _pictures.Values) p.bmp?.Dispose();
        foreach (var l in _layouts.Values) l.Dispose();
        foreach (var g in _shapes.Values) g.geo.Dispose();
        _shadeLin?.Dispose(); _shadeRad?.Dispose(); _softSprite?.Dispose(); _glowSprite?.Dispose();
        foreach (var t in _tilesUnder.Values.Concat(_tilesOver.Values)) t.Dispose();
        _dwrite?.Dispose();
        _round.Dispose(); _brush.Dispose(); _ctx.Target = null; _bitmap?.Dispose(); _ctx.Dispose(); _d2d.Dispose();
        _factory.Dispose(); _visual.Dispose(); _target.Dispose(); _dcomp.Dispose(); _swap.Dispose(); _dxgi.Dispose(); _d3d.Dispose();
    }
}
