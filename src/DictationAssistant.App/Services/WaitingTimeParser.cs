using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App.Services;

public static class WaitingTimeParser
{
    public static IWaitingTimeCalculator Parse(string expression)
    {
        if (int.TryParse(expression.Trim(), out var secs))
            return new FixedWaitingTime(secs);
        return new ExpressionBasedWaitingTime(expression);
    }

    public static bool TryParse(string expression, out IWaitingTimeCalculator? calculator)
    {
        try
        {
            calculator = Parse(expression);
            return true;
        }
        catch
        {
            calculator = null;
            return false;
        }
    }

    public static WaitingTimeValidationResult Validate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new WaitingTimeValidationResult(false, "表达式不能为空", null);
        }

        try
        {
            var calculator = Parse(expression);
            var waitingTime = calculator.CalculateWaitingTime("test");
            if (waitingTime < 0)
            {
                return new WaitingTimeValidationResult(false, "等待时间必须大于0", null);
            }
            return new WaitingTimeValidationResult(true, string.Empty, calculator);
        }
        catch (Exception ex)
        {
            return new WaitingTimeValidationResult(false, $"无效的表达式: {ex.Message}", null);
        }
    }
}

public readonly struct WaitingTimeValidationResult
{
    public bool IsValid { get; }
    public string ErrorMessage { get; }
    public IWaitingTimeCalculator? Calculator { get; }

    public WaitingTimeValidationResult(bool isValid, string errorMessage, IWaitingTimeCalculator? calculator)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        Calculator = calculator;
    }
}
