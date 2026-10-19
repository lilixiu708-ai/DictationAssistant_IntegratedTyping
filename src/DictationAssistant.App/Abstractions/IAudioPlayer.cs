using DictationAssistant.App.Audio;

namespace DictationAssistant.App.Abstractions;

public interface IAudioPlayer
{
    // Volume level from 0 to 100. Default is 100.
    int Volume { get; set; }
    Task PlayAsync(PcmAudio audio, CancellationToken ct);
}
