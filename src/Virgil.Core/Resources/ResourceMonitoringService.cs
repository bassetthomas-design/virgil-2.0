using System.Diagnostics;
using Virgil.Core.Monitoring;
using Virgil.Core.Scanning;
using Virgil.Domain;

namespace Virgil.Core.Resources;

public sealed class ResourceMonitoringService : IResourceMonitoringService
{
    private readonly IProcessInspectionService _processInspectionService;
    private readonly ResourceRecommendationService _recommendationService;
    private readonly Func<TimeSpan, CancellationToken, Task<double>> _cpuReader;
    private readonly Func<MemoryStatus> _memoryReader;
    private readonly Func<TimeSpan> _uptimeProvider;
    private readonly Func<int> _processCountProvider;
    private readonly Func<DateTimeOffset> _now;

    public ResourceMonitoringService()
        : this(
            new ProcessInspectionService(),
            new ResourceRecommendationService(),
            ProcessorReader.MeasureUsageAsync,
            MemoryReader.Read,
            () => TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64)),
            ReadProcessCount,
            () => DateTimeOffset.Now)
    {
    }

    public ResourceMonitoringService(
        IProcessInspectionService processInspectionService,
        ResourceRecommendationService recommendationService,
        Func<TimeSpan, CancellationToken, Task<double>> cpuReader,
        Func<MemoryStatus> memoryReader,
        Func<TimeSpan> uptimeProvider,
        Func<int> processCountProvider,
        Func<DateTimeOffset> now)
    {
        _processInspectionService = processInspectionService;
        _recommendationService = recommendationService;
        _cpuReader = cpuReader;
        _memoryReader = memoryReader;
        _uptimeProvider = uptimeProvider;
        _processCountProvider = processCountProvider;
        _now = now;
    }

    public async Task<ResourceAnalysisReport> AnalyzeAsync(
        ResourceAnalysisRequest request,
        IProgress<ResourceProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var samples = new List<ResourceSample>();
        var interval = TimeSpan.FromTicks(request.ObservationDuration.Ticks / request.SampleCount);
        progress?.Report(new ResourceProgress("Initialisation", 0, "Observation ressources initialisee."));

        var processTask = _processInspectionService.InspectAsync(
            request.ObservationDuration,
            request.MaximumProcesses,
            cancellationToken);

        for (var index = 0; index < request.SampleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cpu = await ReadCpuAsync(interval, errors, cancellationToken).ConfigureAwait(false);
            var memory = ReadMemory(errors);
            var cpuValues = samples.Select(sample => sample.InstantCpuPercent).Append(cpu).ToList();
            var cpuHealth = _recommendationService.ClassifyCpu(cpuValues);
            var memoryHealth = memory.TotalBytes == 0
                ? ResourceHealthLevel.Unknown
                : _recommendationService.ClassifyMemory(memory.UsedPercent);
            var uptime = SafeReadUptime(errors);
            var overall = _recommendationService.Overall(
                cpuHealth,
                memoryHealth,
                _recommendationService.ClassifyUptime(uptime));

            samples.Add(new ResourceSample
            {
                Timestamp = _now(),
                InstantCpuPercent = cpu,
                ShortAverageCpuPercent = cpuValues.Average(),
                TotalMemoryBytes = memory.TotalBytes,
                UsedMemoryBytes = memory.UsedBytes,
                AvailableMemoryBytes = memory.AvailableBytes,
                UsedMemoryPercent = memory.UsedPercent,
                Uptime = uptime,
                ProcessCount = SafeReadProcessCount(errors),
                OverallHealth = overall
            });
            var percent = (int)Math.Round((index + 1d) / request.SampleCount * 80);
            progress?.Report(new ResourceProgress(
                "Observation CPU",
                percent,
                $"Echantillon CPU {index + 1}/{request.SampleCount}."));
        }

        ProcessInspectionResult inspection;
        try
        {
            inspection = await processTask.ConfigureAwait(false);
            errors.AddRange(inspection.Errors);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            inspection = new ProcessInspectionResult();
            errors.Add("Inspection des processus indisponible.");
        }

        progress?.Report(new ResourceProgress("Synthese", 90, "Synthese CPU, RAM et processus."));
        var cpuValuesFinal = samples.Select(sample => sample.InstantCpuPercent).ToList();
        var memoryValues = samples
            .Where(sample => sample.TotalMemoryBytes > 0)
            .Select(sample => sample.UsedMemoryPercent)
            .ToList();
        var cpuHealthFinal = _recommendationService.ClassifyCpu(cpuValuesFinal);
        var memoryHealthFinal = memoryValues.Count == 0
            ? ResourceHealthLevel.Unknown
            : _recommendationService.ClassifyMemory(memoryValues.Average());
        var uptimeFinal = samples.LastOrDefault()?.Uptime ?? TimeSpan.Zero;
        var overallHealth = _recommendationService.Overall(
            cpuHealthFinal,
            memoryHealthFinal,
            _recommendationService.ClassifyUptime(uptimeFinal));
        var processes = inspection.Processes;
        var recommendations = _recommendationService.BuildRecommendations(
            cpuHealthFinal,
            memoryHealthFinal,
            uptimeFinal,
            processes);

        stopwatch.Stop();
        progress?.Report(new ResourceProgress("Termine", 100, "Analyse ressources terminee."));
        return new ResourceAnalysisReport
        {
            CapturedAt = _now(),
            Duration = stopwatch.Elapsed,
            Samples = samples,
            AverageCpuPercent = cpuValuesFinal.Count == 0 ? 0 : cpuValuesFinal.Average(),
            MaximumCpuPercent = cpuValuesFinal.Count == 0 ? 0 : cpuValuesFinal.Max(),
            AverageMemoryPercent = memoryValues.Count == 0 ? 0 : memoryValues.Average(),
            MaximumMemoryPercent = memoryValues.Count == 0 ? 0 : memoryValues.Max(),
            OverallHealth = overallHealth,
            TopMemoryProcesses = processes.OrderByDescending(process => process.WorkingSetBytes).Take(request.MaximumProcesses).ToList(),
            TopCpuProcesses = processes.OrderByDescending(process => process.CpuPercent).Take(request.MaximumProcesses).ToList(),
            Recommendations = recommendations,
            Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private async Task<double> ReadCpuAsync(
        TimeSpan interval,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _cpuReader(interval, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            errors.Add("Mesure CPU indisponible.");
            return 0;
        }
    }

    private MemoryStatus ReadMemory(ICollection<string> errors)
    {
        try
        {
            var memory = _memoryReader();
            if (memory.TotalBytes == 0 || memory.AvailableBytes > memory.TotalBytes)
            {
                errors.Add("Lecture memoire indisponible ou incoherente.");
            }

            return memory;
        }
        catch
        {
            errors.Add("Lecture memoire indisponible.");
            return new MemoryStatus(0, 0);
        }
    }

    private TimeSpan SafeReadUptime(ICollection<string> errors)
    {
        try
        {
            return _uptimeProvider();
        }
        catch
        {
            errors.Add("Uptime indisponible.");
            return TimeSpan.Zero;
        }
    }

    private int SafeReadProcessCount(ICollection<string> errors)
    {
        try
        {
            return Math.Max(0, _processCountProvider());
        }
        catch
        {
            errors.Add("Nombre de processus indisponible.");
            return 0;
        }
    }

    private static int ReadProcessCount()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Length;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void ValidateRequest(ResourceAnalysisRequest request)
    {
        if (request.SampleCount < 2 ||
            request.ObservationDuration <= TimeSpan.Zero ||
            request.MaximumProcesses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Parametres d'observation invalides.");
        }
    }
}
