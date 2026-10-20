// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: keep row-paired questions together when preparing a shuffled round.
namespace DictationAssistant.Practices.Typing;

internal sealed record PracticeQuestion(string Prompt, string Answer, string SourceLabel);

internal sealed record PracticePlan(IReadOnlyList<PracticeQuestion> Questions, bool PlayAudio,
    bool PromptIsChemical, bool ChemicalAnswers, string SourceDescription, bool UsesHostWordList = false,
    bool ShuffleQuestions = false)
{
    public IReadOnlyList<PracticeQuestion> CreateRoundQuestions(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var questions = Questions.ToArray();
        if (ShuffleQuestions && questions.Length > 1)
        {
            for (var index = questions.Length - 1; index > 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var other = Random.Shared.Next(index + 1);
                (questions[index], questions[other]) = (questions[other], questions[index]);
            }
            // A two-question shuffle often returns the original order. When
            // distinct pairs exist, make the requested change visible.
            if (questions.Select(question => (question.Prompt, question.Answer))
                .SequenceEqual(Questions.Select(question => (question.Prompt, question.Answer))))
            {
                var different = Array.FindIndex(questions, 1, question =>
                    question.Prompt != questions[0].Prompt || question.Answer != questions[0].Answer);
                if (different >= 0)
                    (questions[0], questions[different]) = (questions[different], questions[0]);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(questions);
    }
}
