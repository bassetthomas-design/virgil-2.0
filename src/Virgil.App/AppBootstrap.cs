using Virgil.Core.Processes;
using System.Windows;

namespace Virgil.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ProcessRunnerDefaults.Configure(new Virgil.Infrastructure.Processes.ProcessRunner());
        base.OnStartup(e);
    }
}
