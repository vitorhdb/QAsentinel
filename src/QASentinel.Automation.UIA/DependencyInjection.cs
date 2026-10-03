using Microsoft.Extensions.DependencyInjection;
using QASentinel.Automation.Abstractions;

namespace QASentinel.Automation.UIA;

public static class DependencyInjection
{
    public static IServiceCollection AddUia3Automation(this IServiceCollection services)
    {
        services.AddSingleton<Uia3AutomationEngine>();
        services.AddSingleton<IAutomationEngine>(provider => provider.GetRequiredService<Uia3AutomationEngine>());
        services.AddSingleton<IElementResolver>(provider => provider.GetRequiredService<Uia3AutomationEngine>());
        return services;
    }
}
