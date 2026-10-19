using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Lyric;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;

namespace DictationAssistant.App.Services;

public sealed class AudioExporter
{
    private readonly IVoice _voice;
    private readonly IReadOnlyList<string> _words;
    private readonly IWaitingTimeCalculator? _waitingTimeCalculator;

    public AudioExporter(IVoice voice, IReadOnlyList<string> words, IWaitingTimeCalculator? waitingTimeCalculator = null)
    {
        _voice = voice;
        _words = words;
        _waitingTimeCalculator = waitingTimeCalculator;
    }

    public async Task ExportAsync(
        PcmWriter pcmWriter,
        ILyricWriter? lyricWriter,
        int timesPerWord,
        int rate,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timesPerWord, 1);
        if (_words.Count <= 0)
        {
            throw new InvalidOperationException("没有词语可导出");
        }

        var totalWords = _words.Count;
        var totalSegments = (long)totalWords * timesPerWord;
        long currentSegment = 0;
        long byteOffset = 0;

        progress?.Report(0);

        for (var index = 0; index < totalWords; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var word = _words[index];
            var waitingMs = (long)GetWaitingSeconds(word) * 1000;

            var options = new VoiceSynthesisOptions { Rate = rate };
            PcmAudio? pcmAudio = null;
            try
            {
                for (var repeat = 0; repeat < timesPerWord; repeat++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (pcmAudio is not null && pcmAudio.Data.CanSeek)
                    {
                        pcmAudio.Data.Seek(0, SeekOrigin.Begin);
                    }
                    else
                    {
                        // Clear before resynthesis so a failed provider does
                        // not cause a second Dispose of the previous stream.
                        var previousAudio = pcmAudio;
                        pcmAudio = null;
                        previousAudio?.Dispose();
                        pcmAudio = await _voice.SynthesizePcmAsync(word, options, cancellationToken).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("语音引擎未返回音频。");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    lyricWriter?.WriteTimestamp(pcmWriter.BytesToMilliseconds(byteOffset), word);
                    byteOffset += pcmWriter.Write(pcmAudio);
                    byteOffset += pcmWriter.WriteDelay(waitingMs);
                    currentSegment++;
                    progress?.Report((double)currentSegment / totalSegments);
                }
            }
            finally
            {
                pcmAudio?.Dispose();
            }

            // Blank marker after each word (at offset - 10ms), matching v3.x
            lyricWriter?.WriteTimestamp(pcmWriter.BytesToMilliseconds(byteOffset) - 10, " ");
        }

        // Footer marker, matching v3.x
        lyricWriter?.WriteTimestamp(pcmWriter.BytesToMilliseconds(byteOffset), "本文件由 自动默写 程序自动生成");

        progress?.Report(1.0);
    }

    private int GetWaitingSeconds(string word)
    {
        if (_waitingTimeCalculator is not null)
            return Math.Max(0, _waitingTimeCalculator.CalculateWaitingTime(word));
        return 3;
    }
}
