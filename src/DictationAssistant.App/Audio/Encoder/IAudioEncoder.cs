namespace DictationAssistant.App.Audio.Encoder;

public sealed record AudioEncoderInfo(string Name, string Extension)
{
    public override string ToString() => Name;
}

public interface IAudioEncoder
{
    PcmAudio RawAudio { get; }
    Task FinalizeAsync();
}

public interface IAudioEncoderFactory
{
    AudioEncoderInfo Info { get; }
    IAudioEncoder CreateEncoder(PcmFormatInfo format, string path);
}