using System.Collections.Concurrent;
using System.Drawing;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Record a clip: a few seconds of everyone, drawn on paper (only the figures, pets and their things; never
/// what's on your screen), saved as an animated GIF in Pictures\Doodlefolk. Frames are captured offscreen and
/// compressed on a background thread.</summary>
sealed partial class App
{
    sealed class Recording
    {
        public RectangleF Area;
        public float Zoom;
        public int W, H, Fps;
        public double Start, Next, Until;
        public int Frames;
        public string Path = "";
        public readonly BlockingCollection<byte[]> Queue = new(48);
        public Task? Encoder;
    }

    Recording? _rec;

    public string StartRecording(int seconds = 10, string? folder = null)
    {
        if (_rec != null) return "Already recording.";
        // Frame everyone (figures and pets) with some room around them, within one monitor's worth.
        var pts = _w.Figures.Where(f => f.Mode != Mode.Spawning).SelectMany(f => new[] { f.Base, f.Jt[J.Head] })
                    .Concat(_w.Pets.Select(p => p.Pos)).ToList();
        if (pts.Count == 0) return "There's nobody to record.";
        float s = _w.Scale;
        var mon0 = _w.Env.MonBounds.OrderByDescending(m => pts.Count(p => m.Contains((int)p.X, (int)p.Y))).FirstOrDefault(_w.Env.Virtual);
        pts = pts.Where(p => mon0.Contains((int)p.X, (int)p.Y)).DefaultIfEmpty(pts[0]).ToList();
        // Around the busiest stretch: the floor most of them are on.
        float floorY = pts.GroupBy(p => (int)(p.Y / (120 * s))).OrderByDescending(g => g.Count()).First().Average(p => p.Y);
        pts = pts.Where(p => MathF.Abs(p.Y - floorY) < 320 * s).ToList();
        float x0 = pts.Min(p => p.X) - 160 * s, x1 = pts.Max(p => p.X) + 160 * s, y0 = pts.Min(p => p.Y) - 140 * s, y1 = pts.Max(p => p.Y) + 30 * s;
        var mon = mon0;
        x0 = Math.Max(x0, mon.Left); x1 = Math.Min(x1, mon.Right); y0 = Math.Max(y0, mon.Top); y1 = Math.Min(y1, mon.Bottom);
        if (x1 - x0 < 480 * s) { float c = (x0 + x1) / 2; x0 = Math.Max(mon.Left, c - 240 * s); x1 = Math.Min(mon.Right, x0 + 480 * s); }
        if (y1 - y0 < 270 * s) { y0 = Math.Max(mon.Top, y1 - 270 * s); }
        var area = new RectangleF(x0, y0, x1 - x0, y1 - y0);
        float zoom = MathF.Min(1, 960 / area.Width);
        int w = Math.Max(16, (int)(area.Width * zoom)), h = Math.Max(16, (int)(area.Height * zoom));
        string dir = folder ?? AppPaths.PicturesDir;
        Directory.CreateDirectory(dir);
        double now = _clock.Elapsed.TotalSeconds;
        var rec = new Recording
        {
            Area = area, Zoom = zoom, W = w, H = h, Fps = 15, Start = now, Next = now, Until = now + Math.Clamp(seconds, 2, 30),
            Path = System.IO.Path.Combine(dir, $"Doodlefolk clip {DateTime.Now:yyyy-MM-dd HH.mm.ss}.gif"),
        };
        rec.Encoder = Task.Run(() =>
        {
            try
            {
                using var gif = new GifWriter(File.Create(rec.Path), rec.W, rec.H);
                int i = 0;
                foreach (var frame in rec.Queue.GetConsumingEnumerable())
                {
                    // GIF delays are in hundredths: alternate so the average matches the frame rate (15 fps: 7, 7, 6, …).
                    int cs = (int)Math.Round((i + 1) * 100.0 / rec.Fps) - (int)Math.Round(i * 100.0 / rec.Fps);
                    gif.AddFrame(frame, cs * 10);
                    i++;
                }
            }
            catch (Exception e) { World.Log("recording: " + e.Message); }
        });
        _rec = rec;
        World.Log($"recording: {w}x{h} for {seconds}s → {rec.Path}");
        return $"Recording {seconds} seconds…";
    }

    void RecordFrame(double now)
    {
        var rec = _rec;
        if (rec == null || now < rec.Next) return;
        if (now > rec.Until) { StopRecording(); return; }
        rec.Next += 1.0 / rec.Fps;
        if (rec.Next < now) rec.Next = now + 1.0 / rec.Fps;
        var px = new byte[rec.W * rec.H * 4];
        _r.Capture(rec.Area, a => DrawScene(a), Ui.Fill, rec.Zoom, px, rec.W, rec.H);
        // If the encoder falls behind, drop the frame rather than stall the desktop.
        if (rec.Queue.TryAdd(px)) rec.Frames++;
    }

    void StopRecording()
    {
        var rec = _rec;
        if (rec == null) return;
        _rec = null;
        _r.EndCapture();
        rec.Queue.CompleteAdding();
        string path = rec.Path;
        int frames = rec.Frames;
        rec.Encoder?.ContinueWith(_ =>
        {
            World.Log($"recording saved: {frames} frames, {new FileInfo(path).Length / 1024} KB");
            _overlay.BeginInvoke(() => PostAll(new { t = "toast", text = $"Clip saved to Pictures\\Doodlefolk ({frames} frames)" }));
        });
    }
}
