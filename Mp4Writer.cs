using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace Doodlefolk;

/// <summary>Writes an MP4 (H.264 video, AAC audio) with Windows' own Media Foundation encoders: nothing to install.
/// Video frames are top-down BGRA; audio is 16-bit stereo PCM at 44.1 kHz.</summary>
sealed class Mp4Writer : IDisposable
{
    static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");
    readonly IMFSinkWriter _w;
    readonly int _video, _audio = -1, _width, _height, _fps;
    long _frames;

    static ulong Pack(int hi, int lo) => ((ulong)(uint)hi << 32) | (uint)lo;

    public Mp4Writer(string path, int width, int height, int fps, int bitrate, bool audio)
    {
        _width = width; _height = height; _fps = fps;
        MediaFactory.MFStartup();
        if (File.Exists(path)) File.Delete(path);
        // The soundtrack is written at the end, so the muxer must not hold video back waiting for audio to catch up
        // (with throttling on, WriteSample blocks forever after a couple of seconds).
        using var attrs = MediaFactory.MFCreateAttributes(1);
        attrs.Set(SinkWriterAttributeKeys.DisableThrottling, 1u);
        _w = MediaFactory.MFCreateSinkWriterFromURL(path, null!, attrs);

        using (var outV = MediaFactory.MFCreateMediaType())
        {
            outV.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outV.Set(MediaTypeAttributeKeys.Subtype, H264);
            outV.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
            outV.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
            outV.Set(MediaTypeAttributeKeys.FrameSize, Pack(width, height));
            outV.Set(MediaTypeAttributeKeys.FrameRate, Pack(fps, 1));
            outV.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            _video = _w.AddStream(outV);
        }
        using (var inV = MediaFactory.MFCreateMediaType())
        {
            inV.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inV.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            inV.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
            inV.Set(MediaTypeAttributeKeys.FrameSize, Pack(width, height));
            inV.Set(MediaTypeAttributeKeys.FrameRate, Pack(fps, 1));
            inV.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            inV.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(width * 4));   // top-down rows
            _w.SetInputMediaType(_video, inV, null!);
        }
        if (audio)
        {
            using (var outA = MediaFactory.MFCreateMediaType())
            {
                outA.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                outA.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                outA.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                outA.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 44100u);
                outA.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
                outA.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000u);   // 192 kbps
                outA.Set(MediaTypeAttributeKeys.AacPayloadType, 0u);
                _audio = _w.AddStream(outA);
            }
            using var inA = MediaFactory.MFCreateMediaType();
            inA.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            inA.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            inA.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            inA.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 44100u);
            inA.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
            inA.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
            inA.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 176400u);
            _w.SetInputMediaType(_audio, inA, null!);
        }
        _w.BeginWriting();
    }

    void Write(int stream, byte[] data, int length, long time, long duration)
    {
        using var buffer = MediaFactory.MFCreateMemoryBuffer(length);
        buffer.Lock(out IntPtr ptr, out _, out _);
        Marshal.Copy(data, 0, ptr, length);
        buffer.Unlock();
        buffer.CurrentLength = length;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = time;
        sample.SampleDuration = duration;
        _w.WriteSample(stream, sample);
    }

    public void Frame(byte[] bgra)
    {
        long dur = 10_000_000L / _fps;
        Write(_video, bgra, _width * _height * 4, _frames * dur, dur);
        _frames++;
    }

    /// <summary>The whole soundtrack (interleaved stereo floats), written in one-second pieces.</summary>
    public void Audio(float[] stereo)
    {
        if (_audio < 0) return;
        const int chunk = 44100;
        var bytes = new byte[chunk * 4];
        for (int start = 0; start < stereo.Length / 2; start += chunk)
        {
            int n = Math.Min(chunk, stereo.Length / 2 - start);
            for (int i = 0; i < n * 2; i++)
            {
                short v = (short)Math.Clamp(stereo[start * 2 + i] * 32767f, -32767f, 32767f);
                bytes[i * 2] = (byte)v; bytes[i * 2 + 1] = (byte)(v >> 8);
            }
            Write(_audio, bytes, n * 4, start * 10_000_000L / 44100, n * 10_000_000L / 44100);
        }
    }

    public void Dispose()
    {
        try { _w.Finalize(); } finally { _w.Dispose(); MediaFactory.MFShutdown(); }
    }
}
