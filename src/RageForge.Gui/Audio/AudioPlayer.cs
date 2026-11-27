/* RAGEFORGE - Audio Player */

using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using RageForge.Core.Audio;

namespace RageForge.Gui.Audio;

public sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? _out;
    private WaveStream? _stream;

    public event Action? Stopped;

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    public void Play(DecodedAudio audio)
    {
        Stop();
        var bytes = MemoryMarshal.AsBytes(audio.Samples.AsSpan()).ToArray();
        _stream = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(audio.SampleRate, 16, audio.Channels));
        var output = new WaveOutEvent { DesiredLatency = 150 };
        output.PlaybackStopped += (sender, _) =>
        {
            if (ReferenceEquals(sender, _out))
                Stopped?.Invoke();
        };
        output.Init(_stream);
        _out = output;
        output.Play();
    }

    public void Stop()
    {
        var o = _out;
        _out = null;
        o?.Stop();
        o?.Dispose();
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Stop();
}
