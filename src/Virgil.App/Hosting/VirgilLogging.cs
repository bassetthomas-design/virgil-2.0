using System.IO;
using Serilog;
using Serilog.Events;
using Virgil.Contracts;
using Virgil.Infrastructure.Storage;

namespace Virgil.App.Hosting;

public static class VirgilLogging
{
    public static Serilog.Core.Logger CreateLogger(VirgilPaths paths, VirgilSettings settings)
    {
        try { paths.EnsureLogsDirectory(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new LoggerConfiguration().CreateLogger();
        }
        var level = Enum.TryParse<LogEventLevel>(settings.MinimumLogLevel, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : LogEventLevel.Information;
        return new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(paths.LogsDirectory, "virgil-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true, shared: false,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
