namespace DictationAssistant.App.Audio.Encoder;

public class FFmpegMp3EncoderFactory : IAudioEncoderFactory
{
    public AudioEncoderInfo Info { get; } = new AudioEncoderInfo("MPEG Audio Layer 3 (FFmpeg)", "mp3");

    public IAudioEncoder CreateEncoder(PcmFormatInfo format, string path)
    {
        return new ExternalAudioEncoder(format, path, "ffmpeg", $"-f wav -ar {format.SampleRate} -ac {format.Channels} -i pipe:0 -codec:a libmp3lame -q:a 2 -y \"{path}\"");
    }
}

public class FFmpegOpusEncoderFactory : IAudioEncoderFactory
{
    public AudioEncoderInfo Info { get; } = new AudioEncoderInfo("Opus Audio (FFmpeg)", "opus");

    public IAudioEncoder CreateEncoder(PcmFormatInfo format, string path)
    {
        return new ExternalAudioEncoder(format, path, "ffmpeg", $"-f wav -ar {format.SampleRate} -ac {format.Channels} -i pipe:0 -codec:a libopus -b:a 128k -y \"{path}\"");
    }
}