using Microsoft.Extensions.Logging;
using Virgil.App.Hosting;
using Xunit;

namespace Virgil.Tests;

public sealed class ExceptionReporterTests
{
    [Theory]
    [InlineData(false, LogLevel.Error)]
    [InlineData(true, LogLevel.Critical)]
    public void ReportsSourceAndNotifies(bool fatal, LogLevel expected)
    {
        var logger = new RecordingLogger();
        var notifier = new RecordingNotifier();
        new ExceptionReporter(logger, notifier).Report(new InvalidOperationException("private secret"), "TestSource", fatal);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expected, entry.Level);
        Assert.Contains("TestSource", entry.Message);
        Assert.DoesNotContain("private secret", entry.Message);
        Assert.Equal(fatal, Assert.Single(notifier.Calls));
    }

    [Fact]
    public void NotifierFailureIsSwallowedAndLogged()
    {
        var logger = new RecordingLogger();
        var notifier = new RecordingNotifier { Throw = true };
        var error = Record.Exception(() => new ExceptionReporter(logger, notifier).Report(new Exception(), "source", false));
        Assert.Null(error);
        Assert.Single(notifier.Calls);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(LogLevel.Error, logger.Entries[1].Level);
        Assert.Contains("UserNotifier", logger.Entries[1].Message);
    }

    private sealed class RecordingNotifier : IUserNotifier
    {
        public List<bool> Calls { get; } = [];
        public bool Throw { get; init; }
        public void Notify(bool fatal)
        {
            Calls.Add(fatal);
            if (Throw) throw new InvalidOperationException("private notification detail");
        }
    }

    private sealed class RecordingLogger : ILogger<ExceptionReporter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
