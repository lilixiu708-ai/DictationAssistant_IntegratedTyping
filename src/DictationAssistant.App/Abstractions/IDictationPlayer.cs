// Modified 2026-10-19: expose an awaitable reset before editing word order.
using DictationAssistant.App.Models;
using DictationAssistant.App.Lyric;
using DictationAssistant.App.Audio;

namespace DictationAssistant.App.Abstractions;

public interface IDictationPlayer
{
    bool AutoMode { get; set; }
    bool IsSpeaking { get; }
    bool IsPaused { get; }

    IVoice Voice { get; set; }
    IWaitingTimeCalculator? WaitingTimeCalculator { get; set; }
    IWordListSource WordListSource { get; }
    int TimesPerWord { get; set; }
    int Volume { get; set; }
    int Rate { get; set; }

    DictationProgress Progress { get; }

    event EventHandler<DictationProgress>? ProgressChanged;
    event EventHandler<Exception>? PlaybackFailed;

    void SpeakAt(int index, bool resetElapsedTimes = false);
    void SpeakNext();
    void SpeakPrevious();
    void SpeakAgain();

    void StartAuto(int startIndex = 0);
    void PauseAuto();
    void ResumeAuto();
    void Stop();
    Task StopAsync();
    void ResetProgress();

    // Await before replacing the word list; completes after earlier commands and the active chain stop.
    Task ResetProgressAsync();

    // Intended for a modal exercise after ResetProgressAsync; awaited audio drain.
    Task SpeakPracticeWordAsync(string word, CancellationToken cancellationToken);

}
