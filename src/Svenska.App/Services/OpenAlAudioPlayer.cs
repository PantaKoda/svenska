using NVorbis;
using Silk.NET.OpenAL;
using Svenska.Core.Audio;

namespace Svenska.App.Services;

/// <summary>
/// Desktop audio (Windows, Linux, macOS): NVorbis decodes the .ogg file into memory and
/// OpenAL Soft plays it. Clips are single sentences, so decoding the whole file is fine.
/// </summary>
public sealed unsafe class OpenAlAudioPlayer : IAudioPlayer
{
    private readonly ALContext _alc = ALContext.GetApi(soft: true);
    private readonly AL _al = AL.GetApi(soft: true);
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly uint _source;
    private uint _buffer;

    public OpenAlAudioPlayer()
    {
        _device = _alc.OpenDevice("");
        if (_device == null)
            throw new InvalidOperationException("No audio output device found.");
        _context = _alc.CreateContext(_device, null);
        _alc.MakeContextCurrent(_context);
        _source = _al.GenSource();
    }

    public bool IsPlaying
    {
        get
        {
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            return state == (int)SourceState.Playing;
        }
    }

    public void Play(string path)
    {
        Stop();
        var (pcm, channels, sampleRate) = DecodeOgg(path);

        _buffer = _al.GenBuffer();
        fixed (short* data = pcm)
            _al.BufferData(_buffer, channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16,
                data, pcm.Length * sizeof(short), sampleRate);
        _al.SetSourceProperty(_source, SourceInteger.Buffer, _buffer);
        _al.SourcePlay(_source);
    }

    public void Stop()
    {
        _al.SourceStop(_source);
        if (_buffer == 0)
            return;
        _al.SetSourceProperty(_source, SourceInteger.Buffer, 0u);
        _al.DeleteBuffer(_buffer);
        _buffer = 0;
    }

    private static (short[] Pcm, int Channels, int SampleRate) DecodeOgg(string path)
    {
        using var reader = new VorbisReader(path);
        var samples = new float[reader.TotalSamples * reader.Channels];
        var read = 0;
        while (read < samples.Length)
        {
            var n = reader.ReadSamples(samples, read, samples.Length - read);
            if (n <= 0)
                break;
            read += n;
        }

        var pcm = new short[read];
        for (var i = 0; i < read; i++)
            pcm[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
        return (pcm, reader.Channels, reader.SampleRate);
    }

    public void Dispose()
    {
        Stop();
        _al.DeleteSource(_source);
        _alc.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _al.Dispose();
        _alc.Dispose();
    }
}
