// Minimal SDK-only behavioral smoke tests; no xUnit/NUnit download needed.
using DictationAssistant.Practices.Typing;
using System.Diagnostics;
using System.Threading;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    Console.WriteLine("PASS " + description);
}

Check(TypingEvaluation.IsCorrect("Monday", "monday", true), "case-insensitive marking");
Check(!TypingEvaluation.IsCorrect("Monday", "monday", false), "case-sensitive marking");
Check(TypingEvaluation.IsCorrect("café", "cafe\u0301", true), "canonical Unicode equivalence");
Check(!TypingEvaluation.IsCorrect("phenomenon", "phenomena", true), "different answer rejected");
Check(TypingEvaluation.Mask("take part in") == "____   ____   __", "phrase underscore mask");
Check(TypingEvaluation.Mask("one") == "___", "single word underscore mask");
Check(Math.Abs(TypingEvaluation.Accuracy(2, 3) - 66.6666667) < .001, "accuracy calculation");
Check(TypingEvaluation.Accuracy(0, 0) == 0, "zero-total guard");
Check(TypingEvaluation.CsvCell("=SUM(1,2)") == "\"'=SUM(1,2)\"", "CSV formula injection guard");
Check(TypingEvaluation.CsvCell("a,\"b\"") == "\"a,\"\"b\"\"\"", "CSV quote escaping");

// The first await must not finish when speech #1 has ended: repetition #2
// (and its interval) must finish before the typing window may appear.
var gate1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var gate2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var count = 0;
var phases = new List<string>();
var watch = Stopwatch.StartNew();
var waitSecondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var playTask = RepetitionPlayback.PlayAsync("regulate", 2,
    (_, _) =>
    {
        var n = Interlocked.Increment(ref count);
        if (n == 2) waitSecondStarted.TrySetResult();
        return n == 1 ? gate1.Task : gate2.Task;
    },
    _ => TimeSpan.FromMilliseconds(50),
    (r, total, between) => phases.Add($"{r}/{total}:{(between ? "wait" : "play")}"),
    CancellationToken.None);
Check(count == 1 && !playTask.IsCompleted, "waits during first audio playback");
gate1.TrySetResult();
await waitSecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(watch.ElapsedMilliseconds >= 30, "observes inter-repeat delay");
Check(count == 2 && !playTask.IsCompleted, "does not open typing until second audio finishes");
gate2.TrySetResult();
await playTask.WaitAsync(TimeSpan.FromSeconds(5));
Check(phases.SequenceEqual(["1/2:play", "1/2:wait", "2/2:play"]),
    "repetition state machine has no early typing phase");

// Cancellation must not advance a question while an audio provider is running.
using var cancel = new CancellationTokenSource();
var interrupted = RepetitionPlayback.PlayAsync("test", 1,
    (_, token) => Task.Delay(Timeout.Infinite, token), _ => TimeSpan.Zero, null, cancel.Token);
cancel.Cancel();
try
{
    await interrupted;
    throw new Exception("Cancellation did not interrupt playback.");
}
catch (OperationCanceledException) { Console.WriteLine("PASS cancellation stops playback"); }
Console.WriteLine("ALL TYPING LOGIC SMOKE TESTS PASSED");
