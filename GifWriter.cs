namespace StickFight;

/// <summary>A small animated-GIF encoder: each frame gets its own 256-colour palette (the most used colours, which
/// suits flat doodles well), then LZW compression. Frames are added from any one thread at a time.</summary>
sealed class GifWriter : IDisposable
{
    readonly Stream _out;
    readonly int _w, _h;
    bool _first = true;

    public GifWriter(Stream output, int width, int height)
    {
        _out = output; _w = width; _h = height;
        Write("GIF89a");
        Short(_w); Short(_h);
        _out.WriteByte(0x70);       // no global colour table, 8-bit colour resolution
        _out.WriteByte(0); _out.WriteByte(0);
    }

    void Write(string s) { foreach (char c in s) _out.WriteByte((byte)c); }
    void Short(int v) { _out.WriteByte((byte)(v & 0xFF)); _out.WriteByte((byte)(v >> 8)); }

    /// <summary>Add a frame of BGRA pixels (tightly packed, width × height), shown for <paramref name="delayMs"/>.</summary>
    public void AddFrame(byte[] bgra, int delayMs)
    {
        if (_first)
        {
            // Loop forever.
            _out.WriteByte(0x21); _out.WriteByte(0xFF); _out.WriteByte(11); Write("NETSCAPE2.0");
            _out.WriteByte(3); _out.WriteByte(1); Short(0); _out.WriteByte(0);
            _first = false;
        }
        var (palette, indices) = Quantize(bgra);
        // Graphic control: delay, no transparency, leave in place.
        _out.WriteByte(0x21); _out.WriteByte(0xF9); _out.WriteByte(4);
        _out.WriteByte(0x04); Short(Math.Max(2, delayMs / 10)); _out.WriteByte(0); _out.WriteByte(0);
        // Image descriptor with a local 256-colour table.
        _out.WriteByte(0x2C); Short(0); Short(0); Short(_w); Short(_h);
        _out.WriteByte(0x87);
        _out.Write(palette, 0, 768);
        Lzw(indices);
    }

    (byte[] palette, byte[] indices) Quantize(byte[] bgra)
    {
        int n = _w * _h;
        var hist = new int[32768];
        for (int i = 0; i < n; i++) hist[Key(bgra, i * 4)]++;
        var used = new List<int>();
        for (int k = 0; k < hist.Length; k++) if (hist[k] > 0) used.Add(k);
        var top = used.OrderByDescending(k => hist[k]).Take(256).ToArray();
        var palette = new byte[768];
        for (int i = 0; i < top.Length; i++)
        {
            int k = top[i];
            palette[i * 3] = (byte)(((k >> 10) & 31) * 255 / 31);
            palette[i * 3 + 1] = (byte)(((k >> 5) & 31) * 255 / 31);
            palette[i * 3 + 2] = (byte)((k & 31) * 255 / 31);
        }
        // Nearest palette entry for every colour that appears.
        var map = new byte[32768];
        foreach (int k in used)
        {
            int r = (k >> 10) & 31, g = (k >> 5) & 31, b = k & 31, best = 0, bd = int.MaxValue;
            for (int i = 0; i < top.Length; i++)
            {
                int t = top[i];
                int dr = r - ((t >> 10) & 31), dg = g - ((t >> 5) & 31), db = b - (t & 31);
                int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (d < bd) { bd = d; best = i; if (d == 0) break; }
            }
            map[k] = (byte)best;
        }
        var idx = new byte[n];
        for (int i = 0; i < n; i++) idx[i] = map[Key(bgra, i * 4)];
        return (palette, idx);
    }

    static int Key(byte[] p, int o) => ((p[o + 2] >> 3) << 10) | ((p[o + 1] >> 3) << 5) | (p[o] >> 3);

    void Lzw(byte[] data)
    {
        const int minCode = 8, clear = 256, eoi = 257;
        _out.WriteByte(minCode);
        var block = new byte[255];
        int blockLen = 0, bitBuf = 0, bitCount = 0;
        void Flush() { if (blockLen == 0) return; _out.WriteByte((byte)blockLen); _out.Write(block, 0, blockLen); blockLen = 0; }
        void Emit(int code, int size)
        {
            bitBuf |= code << bitCount; bitCount += size;
            while (bitCount >= 8) { block[blockLen++] = (byte)(bitBuf & 0xFF); bitBuf >>= 8; bitCount -= 8; if (blockLen == 255) Flush(); }
        }
        var dict = new Dictionary<int, int>(8192);
        int codeSize = minCode + 1, next = eoi + 1;
        Emit(clear, codeSize);
        int prefix = data.Length > 0 ? data[0] : 0;
        for (int i = 1; i < data.Length; i++)
        {
            int k = data[i];
            int key = (prefix << 8) | k;
            if (dict.TryGetValue(key, out int code)) { prefix = code; continue; }
            Emit(prefix, codeSize);
            if (next < 4096)
            {
                dict[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clear, codeSize);
                dict.Clear(); codeSize = minCode + 1; next = eoi + 1;
            }
            prefix = k;
        }
        Emit(prefix, codeSize);
        Emit(eoi, codeSize);
        if (bitCount > 0) { block[blockLen++] = (byte)(bitBuf & 0xFF); if (blockLen == 255) Flush(); }
        Flush();
        _out.WriteByte(0);
    }

    public void Dispose()
    {
        _out.WriteByte(0x3B);
        _out.Flush();
        _out.Dispose();
    }
}
