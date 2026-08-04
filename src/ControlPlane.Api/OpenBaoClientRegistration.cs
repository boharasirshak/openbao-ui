using ControlPlane.Infrastructure.OpenBao;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api;

public static class OpenBaoClientRegistration
{
    /// <summary>
    /// Every OpenBao-backed service wants the same typed client: base address from
    /// options, standard retry and timeout handling. Registering it in one place keeps
    /// a new service from quietly missing the resilience handler.
    /// </summary>
    public static IServiceCollection AddOpenBaoClient<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services
            .AddHttpClient<TService, TImplementation>((serviceProvider, client) =>
            {
                client.BaseAddress = serviceProvider.GetRequiredService<IOptions<OpenBaoOptions>>().Value.Address;
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
