using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App.Services;

public sealed class FixedWaitingTime : IWaitingTimeCalculator
{
    public int Seconds { get; }
    public FixedWaitingTime(int seconds) => Seconds = seconds;
    public int CalculateWaitingTime(string word) => Seconds;
    public override string ToString() => Seconds.ToString();
}
