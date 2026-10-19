using System.Text;

namespace DictationAssistant.App.Lyric;

public class LyricWriter : ILyricWriter
{
    private readonly Dictionary<string, SortedSet<long>> _lyric = [];

    public LyricWriter()
    {
    }

    public void WriteTimestamp(long ms, string text)
    {
        if (!_lyric.ContainsKey(text))
            _lyric.Add(text, []);
        _lyric[text].Add(ms);
    }

    public void SaveTo(Stream stream)
    {
        using var writer = new StreamWriter(stream, Encoding.Default, 1024, true);
        foreach (var pair in _lyric)
        {
            foreach (var offset in pair.Value)
            {
                var min = offset / 60000;
                var sec = (offset % 60000) / (double)1000;
                writer.Write($"[{min:D2}:{sec:00.00}]");
            }
            writer.WriteLine(pair.Key);
        }
    }
}
