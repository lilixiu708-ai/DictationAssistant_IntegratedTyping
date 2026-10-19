namespace DictationAssistant.App.Abstractions;

public interface IWaitingTimeCalculator
{
    int CalculateWaitingTime(string word);
}
