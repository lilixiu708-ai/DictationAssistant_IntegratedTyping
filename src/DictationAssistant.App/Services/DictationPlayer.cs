// Modified 2026-10-19: serialize playback commands and await cancellation before reordering.
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;
using System.Diagnostics;

namespace DictationAssistant.App.Services;

public sealed class DictationPlayer : IDictationPlayer
{
    private IVoice _voice;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IWordListSource _wordListSource;
    private readonly object _stateLock = new();
    private readonly object _commandLock = new();
    private Task _commandTail = Task.CompletedTask;
    private IWaitingTimeCalculator? _waitingTimeCalculator;
    private Task? _currentChainTask;
    private CancellationTokenSource? _currentChainCts;
    private bool _autoMode;
    private bool _isPaused;
    private int _elapsedTimes;

    public int TimesPerWord { get; set; } = 2;
    public int Volume { get => _audioPlayer.Volume; set => _audioPlayer.Volume = value; }
    public int Rate { get; set; } = 0;

    public DictationPlayer(IVoice voice, IWordListSource wordListSource, IAudioPlayer audioPlayer)
    {
        _voice = voice;
        _audioPlayer = audioPlayer;
        _wordListSource = wordListSource;
        Progress = DictationProgress.Empty with { TotalWords = _wordListSource.Count };
    }

    public bool AutoMode
    {
        get => _autoMode;
        set
        {
            lock (_stateLock)
            {
                _autoMode = value;
            }
        }
    }

    public bool IsSpeaking { get; private set; }

    public bool IsPaused
    {
        get
        {
            lock (_stateLock)
            {
                return _autoMode && _isPaused;
            }
        }
    }

    public IWaitingTimeCalculator? WaitingTimeCalculator
    {
        get => _waitingTimeCalculator;
        set => _waitingTimeCalculator = value;
    }

    public IWordListSource WordListSource => _wordListSource;

    public IVoice Voice
    {
        get => _voice;
        set => _voice = value;
    }

    public DictationProgress Progress { get; private set; }

    public event EventHandler<DictationProgress>? ProgressChanged;
    public event EventHandler<Exception>? PlaybackFailed;

    private TimeSpan GetWaitingTime(string word)
    {
        if (_waitingTimeCalculator is not null)
        {
            var secs = _waitingTimeCalculator.CalculateWaitingTime(word);
            return TimeSpan.FromSeconds(Math.Max(0, secs));
        }
        return TimeSpan.FromSeconds(3);
    }

    public void ResetProgress()
    {
        _ = ResetProgressAsync();
    }

    public Task StopAsync() => ResetProgressAsync();

    public Task ResetProgressAsync()
    {
        return EnqueueCommand(async () =>
        {
            lock (_stateLock)
            {
                _autoMode = false;
                _isPaused = false;
            }

            await CancelChain().ConfigureAwait(false);

            lock (_stateLock)
            {
                _elapsedTimes = 0;
            }

            UpdateProgress(_ => new DictationProgress(-1, 0, _wordListSource.Count, null, null, false));
        });
    }

    public void SpeakNext()
    {
        var target = Math.Clamp(Progress.CurrentWordIndex + 1, 0, Math.Max(0, _wordListSource.Count - 1));
        SpeakAt(target);
    }

    public void SpeakPrevious()
    {
        var target = Math.Max(Progress.CurrentWordIndex - 1, 0);
        SpeakAt(target);
    }

    public void SpeakAgain()
    {
        var target = Progress.CurrentWordIndex >= 0 ? Progress.CurrentWordIndex : 0;
        SpeakAt(target);
    }

    public void SpeakAt(int index, bool resetElapsedTimes = false)
    {
        _ = EnqueueCommand(() => StartSpeakingAsync(index, resetElapsedTimes));
    }

    public void StartAuto(int startIndex = 0)
    {
        _ = EnqueueCommand(() => StartSpeakingAsync(startIndex, resetElapsedTimes: true, startAuto: true));
    }

    public void PauseAuto()
    {
        _ = EnqueueCommand(async () =>
        {
            lock (_stateLock)
            {
                if (!_autoMode)
                {
                    return;
                }

                _isPaused = true;
            }

            await CancelChain().ConfigureAwait(false);
            UpdateProgress(p => p with { NextWordIndex = null, NextSpeakTime = null });
        });
    }

    public void ResumeAuto()
    {
        _ = EnqueueCommand(async () =>
        {
            lock (_stateLock)
            {
                if (!_autoMode || !_isPaused)
                {
                    return;
                }
            }

            await CancelChain().ConfigureAwait(false);

            lock (_stateLock)
            {
                _isPaused = false;
            }

            var cts = new CancellationTokenSource();
            _currentChainCts = cts;
            _currentChainTask = ObservePlaybackAsync(
                ScheduleAndContinueAsync(Progress.CurrentWordIndex, cts.Token), cts.Token);
        });
    }

    public void Stop()
    {
        _ = EnqueueCommand(async () =>
        {
            lock (_stateLock)
            {
                _autoMode = false;
                _isPaused = false;
            }

            await CancelChain().ConfigureAwait(false);
            UpdateProgress(p => p with { NextWordIndex = null, NextSpeakTime = null });
        });
    }

    private async Task StartSpeakingAsync(int index, bool resetElapsedTimes, bool startAuto = false)
    {
        await CancelChain().ConfigureAwait(false);

        var count = _wordListSource.Count;
        if (count <= 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, count - 1);
        lock (_stateLock)
        {
            if (startAuto)
            {
                _autoMode = true;
            }

            _isPaused = false;
            if (resetElapsedTimes || Progress.CurrentWordIndex != index)
            {
                _elapsedTimes = 0;
            }
        }

        var cts = new CancellationTokenSource();
        _currentChainCts = cts;
        _currentChainTask = ObservePlaybackAsync(SpeakChainAsync(index, cts.Token), cts.Token);
    }

    private async Task ObservePlaybackAsync(Task playback, CancellationToken ct)
    {
        try
        {
            await playback.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Replacing or stopping a chain is an expected transition.
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Dictation playback failed: {ex}");
            lock (_stateLock)
            {
                _autoMode = false;
                _isPaused = false;
            }
            IsSpeaking = false;
            try
            {
                UpdateProgress(p => p with { NextWordIndex = null, NextSpeakTime = null });
            }
            catch (Exception notificationError)
            {
                Trace.WriteLine($"Playback progress notification failed: {notificationError}");
            }

            // A faulty subscriber must not create another unobserved task.
            if (PlaybackFailed is { } failed)
            {
                foreach (EventHandler<Exception> handler in failed.GetInvocationList())
                {
                    try
                    {
                        handler(this, ex);
                    }
                    catch (Exception notificationError)
                    {
                        Trace.WriteLine($"Playback failure notification failed: {notificationError}");
                    }
                }
            }
        }
    }

    private Task EnqueueCommand(Func<Task> command)
    {
        // Serialize state transitions, not the duration of a newly started playback chain.
        lock (_commandLock)
        {
            var previous = _commandTail;
            var next = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                await command().ConfigureAwait(false);
            });

            // Observe failures for the void commands without poisoning the queue.
            // Return the original task so an awaited reset can still report its own failure.
            _commandTail = ObserveCommandAsync(next);
            return next;
        }
    }

    private static async Task ObserveCommandAsync(Task command)
    {
        try
        {
            await command.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Dictation command failed: {ex}");
        }
    }

    private async Task CancelChain()
    {
        var cts = _currentChainCts;
        var task = _currentChainTask;
        _currentChainCts = null;
        _currentChainTask = null;

        try
        {
            try
            {
                cts?.Cancel();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Dictation cancellation callback failed: {ex}");
            }

            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when replacing or stopping the current chain.
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Previous dictation chain failed: {ex}");
        }
        finally
        {
            cts?.Dispose();
            task?.Dispose();
        }
    }

    private async Task SpeakChainAsync(int index, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            int currentElapsedTimes;
            lock (_stateLock)
            {
                currentElapsedTimes = _elapsedTimes;
            }

            IsSpeaking = true;
            try
            {
                UpdateProgress(_ => new DictationProgress(index, currentElapsedTimes, _wordListSource.Count, null, null, false));
                await SpeakWordAsync(index, ct).ConfigureAwait(false);
            }
            finally
            {
                IsSpeaking = false;
            }

            lock (_stateLock)
            {
                if (!_autoMode || _isPaused)
                {
                    UpdateProgress(p => p with { NextWordIndex = null, NextSpeakTime = null });
                    return;
                }

                _elapsedTimes += 1;
                currentElapsedTimes = _elapsedTimes;
            }

            int nextIndex;
            if (currentElapsedTimes >= TimesPerWord)
            {
                nextIndex = index + 1;
            }
            else
            {
                nextIndex = index;
            }

            var word = _wordListSource.TryGetAt(index);
            if (word is null)
            {
                lock (_stateLock)
                {
                    _autoMode = false;
                }
                UpdateProgress(p => p with { IsCompleted = true, NextWordIndex = null, NextSpeakTime = null });
                return;
            }

            var waitDuration = GetWaitingTime(word);
            var scheduledTime = DateTimeOffset.Now + waitDuration;

            UpdateProgress(_ => new DictationProgress(index, currentElapsedTimes, _wordListSource.Count, nextIndex, scheduledTime, false));

            if (waitDuration > TimeSpan.Zero)
            {
                await Task.Delay(waitDuration, ct).ConfigureAwait(false);
            }

            lock (_stateLock)
            {
                if (!_autoMode || _isPaused)
                {
                    UpdateProgress(p => p with { NextWordIndex = null, NextSpeakTime = null });
                    return;
                }
            }

            if (nextIndex != index)
            {
                lock (_stateLock)
                {
                    _elapsedTimes = 0;
                }
            }

            index = nextIndex;
        }
    }

    private async Task ScheduleAndContinueAsync(int currentIndex, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        int currentElapsedTimes;
        lock (_stateLock)
        {
            currentElapsedTimes = _elapsedTimes;
        }

        int nextIndex;
        if (currentElapsedTimes >= TimesPerWord)
        {
            nextIndex = currentIndex + 1;
        }
        else
        {
            nextIndex = currentIndex;
        }

        var word = _wordListSource.TryGetAt(currentIndex);
        if (word is null)
        {
            lock (_stateLock)
            {
                _autoMode = false;
            }
            UpdateProgress(p => p with { IsCompleted = true, NextWordIndex = null, NextSpeakTime = null });
            return;
        }

        var waitDuration = GetWaitingTime(word);
        var scheduledTime = DateTimeOffset.Now + waitDuration;

        UpdateProgress(_ => new DictationProgress(currentIndex, currentElapsedTimes, _wordListSource.Count, nextIndex, scheduledTime, false));

        if (waitDuration > TimeSpan.Zero)
        {
            await Task.Delay(waitDuration, ct).ConfigureAwait(false);
        }

        lock (_stateLock)
        {
            if (!_autoMode || _isPaused)
            {
                UpdateProgress(p => p with { NextWordIndex = null, NextSpeakTime = null });
                return;
            }
        }

        if (nextIndex != currentIndex)
        {
            lock (_stateLock)
            {
                _elapsedTimes = 0;
            }
        }

        await SpeakChainAsync(nextIndex, ct).ConfigureAwait(false);
    }

    public async Task SpeakPracticeWordAsync(string word, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        cancellationToken.ThrowIfCancellationRequested();
        using var audio = await _voice.SynthesizePcmAsync(word,
            new VoiceSynthesisOptions { Rate = Rate }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAudioHasData(audio);
        await _audioPlayer.PlayAsync(audio, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureAudioHasData(PcmAudio audio)
    {
        // Built-in providers may return Empty instead of throwing on failure.
        if (ReferenceEquals(audio.Data, Stream.Null) ||
            (audio.Data.CanSeek && audio.Data.Length <= audio.Data.Position))
        {
            throw new InvalidOperationException("语音合成未生成音频：请选择正常工作的语音引擎后重试。");
        }
    }

    private async Task SpeakWordAsync(int index, CancellationToken ct)
    {
        if (index < 0 || index >= _wordListSource.Count)
        {
            return;
        }

        var word = _wordListSource.TryGetAt(index);

        var options = new VoiceSynthesisOptions
        {
            Rate = Rate
        };

        var voice = _voice;
        using var pcmAudio = word is null ? PcmAudio.Empty : await voice.SynthesizePcmAsync(word, options, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(word))
            EnsureAudioHasData(pcmAudio);

        // Preload the next word while the current one is being played, 
        // if supported by the voice and if there is a next word.
        if (voice is IPreloadableVoice preloadable)
        {
            var nextWord = _wordListSource.TryGetAt(index + 1);
            var nextOptions = new VoiceSynthesisOptions
            {
                Rate = Rate
            };

            if (nextWord is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        await preloadable.PreloadAsync(nextWord, nextOptions, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // Cancellation of a replaced chain also cancels its prefetch wait.
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Preload failed for '{nextWord}': {ex}");
                    }
                }, CancellationToken.None);
            }
        }

        await _audioPlayer.PlayAsync(pcmAudio, ct).ConfigureAwait(false);
    }

    private void UpdateProgress(Func<DictationProgress, DictationProgress> mutate)
    {
        Progress = mutate(Progress);
        ProgressChanged?.Invoke(this, Progress);
    }
}
