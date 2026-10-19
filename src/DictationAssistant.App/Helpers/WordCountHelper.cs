namespace DictationAssistant.App.Helpers;

public static class WordCountHelper
{
    public static int GetWordCount(string words)
    {
        string separators = ".,:;?!- ";
        int lastSeparator = 0;
        int count = 1;
        for (int i = 0; i <= words.Length - 1; i++)
        {
            if (separators.IndexOf(words[i]) != -1)
            {
                if (!(lastSeparator == i - 1))
                {
                    if (!(lastSeparator == i - 2 && words[lastSeparator] == '\''))
                        count += 1;
                }
                lastSeparator = i;
            }
        }
        if (lastSeparator == words.Length - 1)
            count -= 1;
        return count;
    }
}
