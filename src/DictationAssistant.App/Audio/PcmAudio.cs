using System;
using System.IO;

namespace DictationAssistant.App.Audio;

public sealed class PcmAudio : IDisposable
{
    public static PcmAudio Empty => new() { Data = Stream.Null, Format = new PcmFormatInfo(44100, 2, PcmSampleFormat.S16LE) };

    public required Stream Data { get; init; }

    public required PcmFormatInfo Format { get; init; }

    public void Dispose()
    {
        Data?.Dispose();
    }
}
