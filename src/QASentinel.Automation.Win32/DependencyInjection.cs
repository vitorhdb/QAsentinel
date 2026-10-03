using Microsoft.Extensions.DependencyInjection;
using QASentinel.Automation.Abstractions;
using QASentinel.Recorder;

namespace QASentinel.Automation.Win32;

public static class DependencyInjection
{
    public static IServiceCollection AddWin32Automation(this IServiceCollection services)
    {
        services.AddSingleton<ITopLevelWindowService, WindowsTopLevelWindowService>();
        services.AddSingleton<IWindowStateService, WindowsWindowStateService>();
        services.AddSingleton<IProcessService, WindowsProcessService>();
        services.AddSingleton<ICursorService, CursorService>();
        services.AddSingleton<IElementHighlighter, WpfElementHighlighter>();
        services.AddSingleton<IScreenshotService, WindowsScreenshotService>();
        services.AddSingleton<IPointerInputSource, WindowsMouseHookSource>();
        return services;
    }
}
