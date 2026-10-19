
namespace DictationAssistant.App.Lyric;

public interface ILyricWriter
{
    void WriteTimestamp(long ms, string text);
    void SaveTo(Stream stream);
}
