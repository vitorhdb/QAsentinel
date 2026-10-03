using Microsoft.Extensions.Logging;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;
using QASentinel.Automation.Win32;

namespace QASentinel.Inspector;

public sealed class HoverInspector(
    IAutomationEngine automationEngine,
    ICursorService cursorService,
    IElementHighlighter highlighter,
    ILogger<HoverInspector> logger) : IHoverInspector
{
    private readonly object _sync = new();
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private InspectorSnapshot _current = new(false, null, "Inspector inativo");

    public InspectorSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public async Task StartAsync(Guid sessionId, TimeSpan interval, CancellationToken cancellationToken)
    {
        if (interval < TimeSpan.FromMilliseconds(80))
        {
            interval = TimeSpan.FromMilliseconds(80);
        }

        await StopAsync(cancellationToken).ConfigureAwait(false);
        var loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _loopCancellation = loopCancellation;
            _current = new InspectorSnapshot(true, null, "Mova o cursor sobre a aplicação conectada");
            _loopTask = RunLoopAsync(sessionId, interval, loopCancellation.Token);
        }

        logger.LogInformation("Operation=StartInspector SessionId={SessionId} IntervalMs={IntervalMs}", sessionId, interval.TotalMilliseconds);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? task;
        CancellationTokenSource? source;
        lock (_sync)
        {
            task = _loopTask;
            source = _loopCancellation;
            _loopTask = null;
            _loopCancellation = null;
            _current = new InspectorSnapshot(false, null, "Inspector inativo");
        }

        source?.Cancel();
        if (task is not null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                logger.LogWarning("Operation=StopInspector Result=Timeout");
            }
        }

        source?.Dispose();
        await highlighter.HideAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(Guid sessionId, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        AutomationElementInfo? lastElement = null;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var position = cursorService.GetPosition();
                var element = await automationEngine
                    .InspectAtAsync(sessionId, position.X, position.Y, cancellationToken)
                    .ConfigureAwait(false);

                if (element is null)
                {
                    if (lastElement is not null)
                    {
                        await highlighter.HideAsync(cancellationToken).ConfigureAwait(false);
                        lastElement = null;
                    }

                    lock (_sync)
                    {
                        _current = new InspectorSnapshot(true, null, "O cursor está fora da aplicação conectada");
                    }

                    continue;
                }

                if (!IsSameElement(lastElement, element))
                {
                    await highlighter.ShowAsync(element.Bounds, cancellationToken).ConfigureAwait(false);
                    lastElement = element;
                    logger.LogDebug(
                        "Operation=InspectElement ProcessId={ProcessId} Element={Element} Backend=UIA3 Result=Success",
                        automationEngine.GetSession(sessionId).ProcessId,
                        element.AutomationId ?? element.Name ?? element.ClassName);
                }

                lock (_sync)
                {
                    _current = new InspectorSnapshot(true, element, "Componente identificado");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=InspectElement SessionId={SessionId} Result=Failure", sessionId);
            lock (_sync)
            {
                _current = new InspectorSnapshot(true, null, "Falha ao consultar o elemento. Consulte os logs.");
            }
        }
    }

    private static bool IsSameElement(AutomationElementInfo? left, AutomationElementInfo right) =>
        left is not null &&
        left.NativeWindowHandle == right.NativeWindowHandle &&
        left.AutomationId == right.AutomationId &&
        left.Name == right.Name &&
        left.Bounds == right.Bounds;

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
