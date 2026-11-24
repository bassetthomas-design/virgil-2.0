using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace Virgil.Tests;

internal static class WpfConditionWait
{
    internal static async Task UntilAsync(
        Func<bool> condition, string expected, Func<string> observed, CancellationToken deadline)
    {
        while (!condition())
        {
            if (deadline.IsCancellationRequested)
                Assert.Fail($"État attendu sur le STA : {expected}. État observé : {observed()}.");
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    internal static async Task LayoutAsync(
        FrameworkElement root, Func<string[]> visibleTexts, CancellationToken deadline, params string[] expected)
    {
        string[] texts = [];
        await UntilAsync(() =>
        {
            root.UpdateLayout();
            texts = visibleTexts();
            return root.IsArrangeValid && root.IsMeasureValid && expected.All(text => texts.Contains(text));
        }, $"mise en page valide et textes [{string.Join(" | ", expected)}]",
            () => $"mesure valide : {root.IsMeasureValid}, disposition valide : {root.IsArrangeValid}, textes [{string.Join(" | ", texts)}]",
            deadline);
    }

    internal static async Task CompletionAsync(Task pending, string step, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await pending.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == deadline.Token)
        {
            Assert.Fail($"Événement attendu : {step}. État observé : {pending.Status} après un plafond de {timeout.TotalSeconds} s.");
        }
    }

    internal static async Task CompletionAsync(Task pending, string step, CancellationToken deadline)
    {
        try { await pending.WaitAsync(deadline); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !pending.IsCompleted)
        {
            Assert.Fail($"Fin attendue sur le STA : {step}. État observé : {pending.Status}.");
        }
    }
}
