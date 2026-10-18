using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using System.Windows;
using System.Windows.Threading;
using Virgil.App.Hosting;
using Virgil.Infrastructure.Settings;
using Virgil.Infrastructure.Storage;

namespace Virgil.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private SerilogLoggerFactory? _bootstrapLogging;
    private ExceptionReporter? _reporter;
    private bool _subscribed;

    protected override void OnStartup(StartupEventArgs e)
    {
        VirgilPaths? paths = null;
        var loggerCreated = false;
        try
        {
            paths = new VirgilPaths();
            var warnings = new List<string>();
            var settings = new JsonSettingsService(paths, warnings.Add);
            Log.Logger = VirgilLogging.CreateLogger(paths, settings.Current);
            loggerCreated = true;
            _bootstrapLogging = new SerilogLoggerFactory(Log.Logger, dispose: false);
            var settingsLogger = _bootstrapLogging.CreateLogger<JsonSettingsService>();
            foreach (var warning in warnings) settingsLogger.LogWarning("{SettingsWarning}", warning);
            var notifier = new MessageBoxUserNotifier(paths, Dispatcher);
            _reporter = new ExceptionReporter(_bootstrapLogging.CreateLogger<ExceptionReporter>(), notifier);
            DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _subscribed = true;
            base.OnStartup(e);
            Log.Information("Demarrage de Virgil {Version}", typeof(App).Assembly.GetName().Version);
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddSerilog(Log.Logger, dispose: true);
            builder.Services.AddVirgilDiagnostics(paths, settings, Dispatcher, _reporter);
            builder.Services.AddVirgilServices();
            builder.Services.AddVirgilViews();
            builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            }));
            _host = builder.Build();
            _host.StartAsync().GetAwaiter().GetResult();
            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            try
            {
                if (loggerCreated)
                    Log.Fatal("Echec du demarrage : {ExceptionType}\n{StackTrace}", ex.GetType().FullName, ex.StackTrace);
            }
            finally
            {
                try
                {
                    MessageBox.Show("Virgil n'a pas pu demarrer.\nDetails enregistres dans les journaux : "
                        + (paths?.LogsDirectory ?? "%LOCALAPPDATA%\\Virgil\\logs"), "Virgil", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { Shutdown(1); }
            }
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _reporter?.Report(e.Exception, "DispatcherUnhandledException", fatal: false);
        e.Handled = true;
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _reporter?.Report(e.ExceptionObject as Exception ?? new Exception(), "AppDomain.UnhandledException", fatal: true);
        }
        finally { Log.CloseAndFlush(); }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Log.Warning("Exception depuis {Source} : {ExceptionType}\n{StackTrace}",
                "TaskScheduler.UnobservedTaskException", e.Exception.GetType().FullName, e.Exception.StackTrace);
        }
        finally { e.SetObserved(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("Arret de Virgil");
            _host?.StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            try { _host?.Dispose(); }
            finally
            {
                if (_subscribed)
                {
                    DispatcherUnhandledException -= OnDispatcherException;
                    AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
                    TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                }
                try { _bootstrapLogging?.Dispose(); }
                finally
                {
                    try { Log.CloseAndFlush(); }
                    finally { base.OnExit(e); }
                }
            }
        }
    }
}
