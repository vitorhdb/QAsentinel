using QASentinel.Automation.Models;

namespace QASentinel.Inspector;

public sealed record InspectorSnapshot(bool IsRunning, AutomationElementInfo? Element, string? StatusMessage);

public interface IHoverInspector : IAsyncDisposable
{
    InspectorSnapshot Current { get; }

    Task StartAsync(Guid sessionId, TimeSpan interval, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
