using QASentinel.Automation.Models;

namespace QASentinel.Automation.Abstractions;

public interface IProcessService
{
    Task<IReadOnlyList<ProcessDescriptor>> GetProcessesAsync(CancellationToken cancellationToken);
}

public interface ITopLevelWindowService
{
    IReadOnlyList<TopLevelWindowDescriptor> GetWindows(int processId);
}

public interface IWindowStateService
{
    WindowPresentationState GetState(nint windowHandle);

    void Restore(nint windowHandle);

    void Minimize(nint windowHandle);

    bool Activate(nint windowHandle);
}

public interface IAutomationEngine : IAsyncDisposable
{
    Task<ApplicationSession> AttachAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken);

    Task<AutomationElementInfo?> InspectAtAsync(Guid sessionId, double x, double y, CancellationToken cancellationToken);

    Task<AutomationElementInfo?> InspectActionableAtAsync(
        Guid sessionId,
        double x,
        double y,
        CancellationToken cancellationToken);

    Task<AutomationElementInfo?> FindElementAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken);

    Task<AutomationElementInfo?> GetFocusedElementAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<AutomationValueInfo> GetValueAsync(
        Guid sessionId,
        LocatorSet locators,
        CancellationToken cancellationToken);

    Task<AutomationCompatibilityScan> ScanCompatibilityAsync(
        Guid sessionId,
        CompatibilityScanOptions options,
        CancellationToken cancellationToken);

    Task ClickAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken);

    Task PhysicalClickAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken);

    Task FocusAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken);

    Task TypeTextAsync(Guid sessionId, LocatorSet locators, string text, CancellationToken cancellationToken);

    ApplicationSession GetSession(Guid sessionId);
}

public interface IElementResolver
{
    Task<AutomationElementInfo?> ResolveAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken);
}

public interface IElementHighlighter : IAsyncDisposable
{
    Task ShowAsync(BoundingRectangle bounds, CancellationToken cancellationToken);

    Task HideAsync(CancellationToken cancellationToken);
}

public interface IScreenshotService
{
    Task<ScreenshotResult> CaptureAsync(
        ScreenshotTarget target,
        ApplicationSession session,
        AutomationElementInfo? element,
        string outputDirectory,
        CancellationToken cancellationToken);
}

public interface IComponentAdapter
{
    bool CanHandle(AutomationElementInfo element);
}
