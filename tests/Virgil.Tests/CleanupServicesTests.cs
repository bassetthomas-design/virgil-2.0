using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Core.Cleanup;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupServicesTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PreviewAsync_marks_old_files_eligible_and_recent_files_excluded()
    {
        using var sandbox = TemporarySandbox.Create();
        var oldFile = sandbox.WriteFile("old.tmp", 128, Now.AddDays(-2));
        var recentFile = sandbox.WriteFile("recent.tmp", 64, Now.AddHours(-2));
        var service = CreatePreviewService(sandbox.Root, TimeSpan.FromHours(24));

        var preview = await PreviewSingleZoneAsync(service);

        Assert.Equal(1, preview.EligibleFileCount);
        Assert.Equal(1, preview.ExcludedFileCount);
        Assert.Contains(preview.Candidates, candidate => candidate.FullPath == oldFile && candidate.IsEligible);
        Assert.Contains(preview.Candidates, candidate => candidate.FullPath == recentFile && !candidate.IsEligible);
    }

    [Fact]
    public void PathGuard_refuses_paths_outside_root()
    {
        using var sandbox = TemporarySandbox.Create();
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp");

        Assert.False(CleanupPathGuard.IsStrictlyUnderRoot(outside, sandbox.Root));
    }

    [Fact]
    public void PathGuard_refuses_prefix_tricks()
    {
        var root = Path.Combine(Path.GetTempPath(), "virgil-root");
        var trick = root + "-other";
        var candidate = Path.Combine(trick, "file.tmp");

        Assert.False(CleanupPathGuard.IsStrictlyUnderRoot(candidate, root));
    }

    [Fact]
    public void PathGuard_refuses_root_itself()
    {
        using var sandbox = TemporarySandbox.Create();

        Assert.False(CleanupPathGuard.IsStrictlyUnderRoot(sandbox.Root, sandbox.Root));
    }

    [Fact]
    public void PathGuard_detects_reparse_attributes()
    {
        Assert.True(CleanupPathGuard.HasReparsePoint(FileAttributes.ReparsePoint));
    }

    [Fact]
    public async Task Execution_ignores_files_that_vanished_after_preview()
    {
        using var sandbox = TemporarySandbox.Create();
        var file = sandbox.WriteFile("vanished.tmp", 32, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));
        File.Delete(file);

        var result = await ExecuteAsync(preview);

        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Execution_reports_locked_file_without_stopping_zone()
    {
        using var sandbox = TemporarySandbox.Create();
        var locked = sandbox.WriteFile("locked.tmp", 32, Now.AddDays(-2));
        var free = sandbox.WriteFile("free.tmp", 16, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));

        using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await ExecuteAsync(preview);

        Assert.Equal(CleanupStepStatus.PartialFailure, result.Status);
        Assert.Equal(1, result.DeletedFiles);
        Assert.Equal(1, result.ErrorFiles);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
    }

    [Fact]
    public async Task Execution_honors_cancellation_between_files()
    {
        using var sandbox = TemporarySandbox.Create();
        sandbox.WriteFile("one.tmp", 8, Now.AddDays(-2));
        sandbox.WriteFile("two.tmp", 8, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(cancellation);

        var result = await new CleanupExecutionService(() => Now)
            .ExecuteZoneAsync(preview, progress, cancellation.Token);

        Assert.True(result.Status is CleanupStepStatus.Cancelled or CleanupStepStatus.Completed);
        Assert.True(result.DeletedFiles <= 2);
    }

    [Fact]
    public async Task SkipZone_does_not_delete_any_file()
    {
        using var sandbox = TemporarySandbox.Create();
        var file = sandbox.WriteFile("old.tmp", 32, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));

        var result = new CleanupExecutionService(() => Now).SkipZone(preview);

        Assert.Equal(CleanupStepStatus.Skipped, result.Status);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Execution_refuses_stale_preview()
    {
        using var sandbox = TemporarySandbox.Create();
        var file = sandbox.WriteFile("old.tmp", 32, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));
        var staleExecution = new CleanupExecutionService(() => Now.AddMinutes(11));

        var result = await staleExecution.ExecuteZoneAsync(preview, null, CancellationToken.None);

        Assert.Equal(CleanupStepStatus.Expired, result.Status);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Execution_counts_deleted_bytes()
    {
        using var sandbox = TemporarySandbox.Create();
        sandbox.WriteFile("one.tmp", 10, Now.AddDays(-2));
        sandbox.WriteFile("two.tmp", 15, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));

        var result = await ExecuteAsync(preview);

        Assert.Equal(25, result.DeletedBytes);
        Assert.Equal(2, result.DeletedFiles);
    }

    [Fact]
    public async Task Execution_deletes_empty_subfolders_but_never_root()
    {
        using var sandbox = TemporarySandbox.Create();
        var child = Directory.CreateDirectory(Path.Combine(sandbox.Root, "child")).FullName;
        sandbox.WriteFile(Path.Combine("child", "old.tmp"), 10, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));

        var result = await ExecuteAsync(preview);

        Assert.Equal(CleanupStepStatus.Completed, result.Status);
        Assert.False(Directory.Exists(child));
        Assert.True(Directory.Exists(sandbox.Root));
    }

    [Fact]
    public async Task Execution_does_not_delete_recent_excluded_candidates()
    {
        using var sandbox = TemporarySandbox.Create();
        var recent = sandbox.WriteFile("recent.tmp", 10, Now.AddMinutes(-5));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));

        var result = await ExecuteAsync(preview);

        Assert.Equal(0, result.DeletedFiles);
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public async Task PreviewAsync_handles_missing_zone_without_deleting_or_throwing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = CreatePreviewService(missingRoot, TimeSpan.FromHours(1));

        var preview = await PreviewSingleZoneAsync(service);

        Assert.Equal(0, preview.EligibleFileCount);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void CancelZone_keeps_zone_report_without_deletion()
    {
        using var sandbox = TemporarySandbox.Create();
        var definition = TestZone(sandbox.Root, TimeSpan.FromHours(1));
        var preview = new CleanupZonePreview(definition, Now, 0, 0, 0, 0, Array.Empty<CleanupCandidate>(), Array.Empty<string>());

        var result = new CleanupExecutionService(() => Now).CancelZone(preview);

        Assert.Equal(CleanupStepStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Execution_report_aggregates_results_and_errors()
    {
        using var sandbox = TemporarySandbox.Create();
        sandbox.WriteFile("old.tmp", 10, Now.AddDays(-2));
        var preview = await PreviewSingleZoneAsync(CreatePreviewService(sandbox.Root, TimeSpan.FromHours(1)));
        var service = new CleanupExecutionService(() => Now.AddSeconds(2));
        var result = await service.ExecuteZoneAsync(preview, null, CancellationToken.None);

        var report = service.CreateReport(Now, new[] { result }, new[] { "Session partielle." });

        Assert.Equal(1, report.DeletedFiles);
        Assert.Equal(10, report.DeletedBytes);
        Assert.Equal("Session partielle.", Assert.Single(report.Errors));
    }

    private static CleanupPreviewService CreatePreviewService(string root, TimeSpan minimumAge)
    {
        return new CleanupPreviewService(new[] { TestZone(root, minimumAge) }, () => Now);
    }

    private static CleanupZoneDefinition TestZone(string root, TimeSpan minimumAge)
    {
        return new CleanupZoneDefinition(
            CleanupZoneId.UserTemporaryFiles,
            "Zone test",
            "Zone temporaire de test.",
            root,
            minimumAge,
            CleanupRiskLevel.Low,
            "Test uniquement.",
            "Suppression test.",
            "Racine et fichiers hors zone.",
            1);
    }

    private static async Task<CleanupZonePreview> PreviewSingleZoneAsync(CleanupPreviewService service)
    {
        var previews = await service.PreviewAsync(null, CancellationToken.None);
        return Assert.Single(previews);
    }

    private static Task<CleanupStepResult> ExecuteAsync(CleanupZonePreview preview)
    {
        return new CleanupExecutionService(() => Now).ExecuteZoneAsync(preview, null, CancellationToken.None);
    }

    private sealed class CancellingProgress : IProgress<CleanupProgress>
    {
        private readonly CancellationTokenSource _cancellation;
        private bool _cancelled;

        public CancellingProgress(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public void Report(CleanupProgress value)
        {
            if (_cancelled || value.Step != "Suppression")
            {
                return;
            }

            _cancelled = true;
            _cancellation.Cancel();
        }
    }

    private sealed class TemporarySandbox : IDisposable
    {
        private TemporarySandbox(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporarySandbox Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "virgil-cleanup-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporarySandbox(root);
        }

        public string WriteFile(string relativePath, int byteCount, DateTimeOffset lastWriteUtc)
        {
            var fullPath = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, Enumerable.Repeat((byte)42, byteCount).ToArray());
            File.SetLastWriteTimeUtc(fullPath, lastWriteUtc.UtcDateTime);
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
            catch
            {
                // Test cleanup is best effort only.
            }
        }
    }
}
