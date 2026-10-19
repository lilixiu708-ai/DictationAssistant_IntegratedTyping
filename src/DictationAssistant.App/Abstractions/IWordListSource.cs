namespace DictationAssistant.App.Abstractions;

public interface IWordListSource
{
    int Count { get; }

    string this[int index]
    {
        get
        {
            string? result = TryGetAt(index);
            if (result is not null) return result;
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    string? TryGetAt(int index);

    IReadOnlyList<string> GetWords();
}
