using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using Virgil.App.Hosting;

namespace Virgil.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddVirgilServices();
        builder.Services.AddVirgilViews();
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        }));
        _host = builder.Build();
        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
