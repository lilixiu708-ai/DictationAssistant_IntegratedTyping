namespace DictationAssistant.App.Audio.Encoder;


public sealed class WaveEncoderFactory : IAudioEncoderFactory
{
    private static readonly AudioEncoderInfo _info = new("Waveform Audio", "wav");
    public AudioEncoderInfo Info { get; } = _info;

    public IAudioEncoder CreateEncoder(PcmFormatInfo format, string path)
    {
        return new WaveEncoder(format, path);
    }

    public override string ToString() => Info.Name;
}
