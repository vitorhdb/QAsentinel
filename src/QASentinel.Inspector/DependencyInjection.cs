using Microsoft.Extensions.DependencyInjection;

namespace QASentinel.Inspector;

public static class DependencyInjection
{
    public static IServiceCollection AddInspector(this IServiceCollection services)
    {
        services.AddSingleton<IHoverInspector, HoverInspector>();
        return services;
    }
}
