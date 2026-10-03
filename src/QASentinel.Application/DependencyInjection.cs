using Microsoft.Extensions.DependencyInjection;
using QASentinel.Application.Executions;

namespace QASentinel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IExecutionDelay, SystemExecutionDelay>();
        services.AddSingleton<ITestRunner, TestRunner>();
        return services;
    }
}
