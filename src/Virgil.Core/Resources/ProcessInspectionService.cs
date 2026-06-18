using Virgil.Domain;

namespace Virgil.Core.Resources;

public sealed class ProcessInspectionService : IProcessInspectionService
{
    private readonly IProcessSnapshotProvider _snapshotProvider;
    private readonly ProcessProtectionPolicy _protectionPolicy;
    private readonly int _logicalProcessorCount;

    public ProcessInspectionService()
        : this(new ProcessSnapshotProvider(), new ProcessProtectionPolicy(), Environment.ProcessorCount)
    {
    }

    public ProcessInspectionService(
        IProcessSnapshotProvider snapshotProvider,
        ProcessProtectionPolicy protectionPolicy,
        int logicalProcessorCount)
    {
        _snapshotProvider = snapshotProvider;
        _protectionPolicy = protectionPolicy;
        _logicalProcessorCount = Math.Max(1, logicalProcessorCount);
    }

    public async Task<ProcessInspectionResult> InspectAsync(
        TimeSpan observationDuration,
        int maximumProcesses,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = _snapshotProvider.Capture();
        await Task.Delay(observationDuration, cancellationToken).ConfigureAwait(false);
        var second = _snapshotProvider.Capture();
        var firstById = first.Processes.ToDictionary(process => process.ProcessId);
        var candidates = new List<(ProcessObservation Process, double Cpu)>();

        foreach (var process in second.Processes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cpu = CalculateCpuPercent(firstById, process, observationDuration);
            candidates.Add((process, cpu));
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.Process.WorkingSetBytes)
            .Take(maximumProcesses)
            .Concat(candidates.OrderByDescending(candidate => candidate.Cpu).Take(maximumProcesses))
            .GroupBy(candidate => candidate.Process.ProcessId)
            .Select(group => group.First())
            .ToList();
        var processes = selected
            .Select(candidate => BuildInfo(candidate.Process, candidate.Cpu))
            .OrderByDescending(process => process.Status == ProcessResourceStatus.Heavy)
            .ThenByDescending(process => process.WorkingSetBytes)
            .ToList();

        return new ProcessInspectionResult
        {
            Processes = processes,
            Errors = first.Errors.Concat(second.Errors).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private ProcessResourceInfo BuildInfo(ProcessObservation observation, double cpuPercent)
    {
        var publisher = _snapshotProvider.TryGetPublisher(observation.Path);
        var enriched = observation with { Publisher = publisher };
        var protection = _protectionPolicy.Evaluate(enriched, cpuPercent);
        return new ProcessResourceInfo
        {
            ProcessId = observation.ProcessId,
            Name = observation.Name,
            MainWindowTitle = observation.MainWindowTitle,
            Path = observation.Path,
            Publisher = publisher,
            WorkingSetBytes = observation.WorkingSetBytes,
            CpuPercent = cpuPercent,
            Status = protection.Status,
            CanCloseGracefully = protection.CanCloseGracefully,
            CanForceClose = protection.CanForceClose,
            IsCriticalSystemProcess = protection.IsCritical,
            UserMessage = protection.Message,
            StartedAt = observation.StartedAt
        };
    }

    private double CalculateCpuPercent(
        IReadOnlyDictionary<int, ProcessObservation> firstById,
        ProcessObservation second,
        TimeSpan observationDuration)
    {
        if (!firstById.TryGetValue(second.ProcessId, out var first) ||
            !SameIdentity(first, second) ||
            observationDuration <= TimeSpan.Zero)
        {
            return 0;
        }

        var delta = second.TotalProcessorTime - first.TotalProcessorTime;
        if (delta <= TimeSpan.Zero)
        {
            return 0;
        }

        var percent = delta.TotalMilliseconds /
            (observationDuration.TotalMilliseconds * _logicalProcessorCount) * 100;
        return Math.Round(Math.Clamp(percent, 0, 100), 1);
    }

    private static bool SameIdentity(ProcessObservation first, ProcessObservation second)
    {
        return string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase) &&
            (first.StartedAt is null || second.StartedAt is null || first.StartedAt == second.StartedAt);
    }
}
