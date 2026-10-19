using Microsoft.Extensions.DependencyInjection;
using Virgil.Core.Actions;

namespace Virgil.App.Hosting;

public static class ConfirmationServiceCollectionExtensions
{
    public static IServiceCollection AddVirgilConfirmation(this IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.AddSingleton<ConfirmationService>(sp => new ConfirmationService(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IConfirmationService>(sp => sp.GetRequiredService<ConfirmationService>());
        services.AddSingleton<IConfirmedPlanGate>(sp => sp.GetRequiredService<ConfirmationService>());
        return services;
    }
}
