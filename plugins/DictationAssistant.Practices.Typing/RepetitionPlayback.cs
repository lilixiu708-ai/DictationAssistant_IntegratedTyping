// DictationAssistant synchronous typing practice, AGPL-3.0-or-later.
namespace DictationAssistant.Practices.Typing;

/// <summary>
/// A single word becomes available for typing only AFTER this method returns;
/// it awaits every actual SpeakOnce completion and every intervening delay.
/// </summary>
public static class RepetitionPlayback
{
    public static async Task PlayAsync(
        string word,
        int timesPerWord,
        Func<string, CancellationToken, Task> speakOnce,
        Func<string, TimeSpan> getRepeatInterval,
        Action<int, int, bool>? onPhase,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentNullException.ThrowIfNull(speakOnce);
        ArgumentNullException.ThrowIfNull(getRepeatInterval);
        if (timesPerWord < 1 || timesPerWord > 100)
            throw new ArgumentOutOfRangeException(nameof(timesPerWord));

        for (var repeat = 1; repeat <= timesPerWord; repeat++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onPhase?.Invoke(repeat, timesPerWord, false);
            await speakOnce(word, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (repeat == timesPerWord) continue;

            var delay = getRepeatInterval(word);
            if (delay < TimeSpan.Zero || delay > TimeSpan.FromHours(1))
                throw new InvalidOperationException("重复播报间隔必须在 0 秒到 1 小时之间。");
            onPhase?.Invoke(repeat, timesPerWord, true);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }
    }
}
