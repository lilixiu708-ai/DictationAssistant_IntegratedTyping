using System.IO;

namespace DictationAssistant.App.Audio;

public static class BassDecoder
{
    public static PcmAudio FromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            options: FileOptions.SequentialScan);

        var decodeStream = new BassDecodeStream(fileStream, leaveOpen: false);
        return new PcmAudio
        {
            Data = decodeStream,
            Format = decodeStream.Format
        };
    }

    public static PcmAudio FromStream(Stream inputStream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        var decodeStream = new BassDecodeStream(inputStream, leaveOpen);
        return new PcmAudio
        {
            Data = decodeStream,
            Format = decodeStream.Format
        };
    }
}
