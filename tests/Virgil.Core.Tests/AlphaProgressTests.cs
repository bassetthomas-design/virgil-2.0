using System.Text.Json;
using Virgil.Contracts;
using Virgil.Core.Interventions;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

[Trait("Category", "Windows")]
public sealed class AlphaProgressTests
{
    [Fact]
    public async Task ClientStreamsNonceBoundProgressAndCancelWaitsForHelperResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "VirgilProgressTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var helperPath = Path.Combine(root, "helper.exe");
            await File.WriteAllTextAsync(helperPath, "test executable marker");
            var store = new ElevatedHelperRequestStore(() => root, new ElevatedProtocolRoot(), new ElevatedProtocolFiles(),
                () => System.Security.Cryptography.RandomNumberGenerator.GetBytes(16), () => DateTimeOffset.UtcNow);
            var launcher = new Launcher();
            var client = new ElevatedHelperClient(store, launcher, () => helperPath);
            using var cancellation = new CancellationTokenSource();
            var progress = new ProgressSink();
            var pending = client.ExecuteAsync(new InterventionCatalog().Get(InterventionId.VerifyHealth), progress, cancellation.Token);
            await progress.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(progress.Values, item => item.Step == "SFC" && item.Percent == 31);
            cancellation.Cancel();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(launcher.CancelObserved);
            Assert.True(launcher.Exited);
            Assert.Equal(InterventionStatus.Cancelled, result.Status);
            Assert.False(File.Exists(launcher.CancelPath)); // Cleanup occurs only after helper exit.
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ProgressRejectsWrongNonce()
    {
        var root = Path.Combine(Path.GetTempPath(), "VirgilProgressTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ElevatedHelperRequestStore(() => root, new ElevatedProtocolRoot(), new ElevatedProtocolFiles(),
                () => System.Security.Cryptography.RandomNumberGenerator.GetBytes(16), () => DateTimeOffset.UtcNow);
            var request = await store.CreateAsync(InterventionId.VerifyHealth, CancellationToken.None);
            await File.WriteAllTextAsync(request.ResultPath.Replace(".result.json", ".progress.json", StringComparison.Ordinal),
                JsonSerializer.Serialize(new InterventionProgress { Nonce = "OTHER", Step = "Forged" }));
            Assert.Null(await store.ReadProgressAsync(request, CancellationToken.None));
            store.Cleanup(request);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ProgressSink : IProgress<InterventionProgress>
    {
        public List<InterventionProgress> Values { get; } = new();
        public TaskCompletionSource<bool> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Report(InterventionProgress value)
        {
            Values.Add(value);
            if (value.Step == "SFC") Received.TrySetResult(true);
        }
    }

    private sealed class Launcher : IElevatedProcessLauncher
    {
        public bool CancelObserved { get; private set; }
        public bool Exited { get; private set; }
        public string CancelPath { get; private set; } = string.Empty;
        public async Task<int> RunElevatedAsync(string helperPath, string requestPath, CancellationToken token)
        {
            var request = JsonSerializer.Deserialize<ElevatedInterventionRequest>(await File.ReadAllTextAsync(requestPath, token))!;
            CancelPath = request.ResultPath.Replace(".result.json", ".cancel", StringComparison.Ordinal);
            var progressPath = request.ResultPath.Replace(".result.json", ".progress.json", StringComparison.Ordinal);
            await File.WriteAllTextAsync(progressPath, JsonSerializer.Serialize(new InterventionProgress
            { Nonce = request.Nonce, Step = "SFC", Percent = 31, Elapsed = TimeSpan.FromSeconds(2) }), token);
            while (!File.Exists(CancelPath)) await Task.Delay(20, token);
            CancelObserved = true;
            await File.WriteAllTextAsync(request.ResultPath, JsonSerializer.Serialize(new ElevatedInterventionResult
            {
                ActionId = request.ActionId, Nonce = request.Nonce, StartedAt = request.CreatedAt,
                FinishedAt = DateTimeOffset.UtcNow, Status = InterventionStatus.Cancelled, SummaryOutput = "Annulation observée."
            }), token);
            Exited = true;
            return 0;
        }
    }
}
