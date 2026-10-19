using System;
using System.Text.RegularExpressions;
using NCalc;
using NCalc.Handlers;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Helpers;

namespace DictationAssistant.App.Services;

public sealed class ExpressionBasedWaitingTime : IWaitingTimeCalculator
{
    private readonly string _expressionText;
    private readonly Expression _expressionObject;

    public ExpressionBasedWaitingTime(string expressionText)
    {
        _expressionText = expressionText;
        _expressionObject = new Expression(expressionText);
        _expressionObject.EvaluateFunction += EvaluateFunctionHandler;
        CalculateWaitingTime("foo bar");
    }

    private static void EvaluateFunctionHandler(string name, FunctionArgs args)
    {
        var p = args.Parameters;
        switch (name.ToLower())
        {
            case "length":
                args.Result = (p[0].Evaluate()?.ToString() ?? "").Length;
                break;
            case "wordcount":
                args.Result = WordCountHelper.GetWordCount(p[0].Evaluate()?.ToString() ?? "");
                break;
            case "regexreplace":
                args.Result = Regex.Replace(
                    p[0].Evaluate()?.ToString() ?? "",
                    p[1].Evaluate()?.ToString() ?? "",
                    p[2].Evaluate()?.ToString() ?? "",
                    p.Length >= 4 && Convert.ToBoolean(p[3].Evaluate())
                        ? RegexOptions.IgnoreCase
                        : RegexOptions.None);
                break;
            case "regexmatch":
                args.Result = Regex.IsMatch(
                    p[0].Evaluate()?.ToString() ?? "",
                    p[1].Evaluate()?.ToString() ?? "",
                    p.Length >= 3 && Convert.ToBoolean(p[2].Evaluate())
                        ? RegexOptions.IgnoreCase
                        : RegexOptions.None);
                break;
        }
    }

    public int CalculateWaitingTime(string word)
    {
        void EvaluateParameterCallback(string name, ParameterArgs args)
        {
            switch (name.ToLower())
            {
                case "length":
                    args.Result = word.Length;
                    break;
                case "wordcount":
                    args.Result = WordCountHelper.GetWordCount(word);
                    break;
                case "text":
                    args.Result = word;
                    break;
            }
        }

        _expressionObject.EvaluateParameter += EvaluateParameterCallback;
        var returnValue = Convert.ToInt32(_expressionObject.Evaluate());
        _expressionObject.EvaluateParameter -= EvaluateParameterCallback;
        return returnValue;
    }

    public override string ToString() => _expressionText;
}
