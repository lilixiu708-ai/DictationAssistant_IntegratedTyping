// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: immutable, row-paired questions selected before practice.
namespace DictationAssistant.Practices.Typing;

internal sealed record PracticeQuestion(string Prompt, string Answer, string SourceLabel);

internal sealed record PracticePlan(IReadOnlyList<PracticeQuestion> Questions, bool PlayAudio,
    bool PromptIsChemical, bool ChemicalAnswers, string SourceDescription, bool UsesHostWordList = false);
