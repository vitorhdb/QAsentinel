using Microsoft.Extensions.DependencyInjection;

namespace QASentinel.Recorder;

public static class DependencyInjection
{
    public static IServiceCollection AddRecorder(this IServiceCollection services)
    {
        services.AddSingleton<IDesktopInputRecorder, DesktopInputRecorder>();
        return services;
    }
}
