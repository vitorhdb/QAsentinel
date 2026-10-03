using QASentinel.Application.Executions;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Exceptions;
using QASentinel.Automation.Models;
using QASentinel.Domain.Testing;

namespace QASentinel.Agent;

public sealed class AgentTestAutomationDriver(
    IAutomationEngine automationEngine,
    ITopLevelWindowService windowService) : ITestAutomationDriver
{
    public async Task<AutomationElementState?> FindAsync(
        Guid sessionId,
        TestTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            var element = await automationEngine.FindElementAsync(
                sessionId,
                target.ToAutomationLocatorSet(),
                cancellationToken).ConfigureAwait(false);
            if (element is null)
            {
                return null;
            }

            var session = automationEngine.GetSession(sessionId);
            var window = windowService.GetWindows(session.ProcessId)
                .FirstOrDefault(candidate => candidate.Handle == session.MainWindowHandle);
            var hasPhysicalIntersection = window is not null && Intersects(element.Bounds, window.Bounds);
            return new AutomationElementState(
                true,
                element.IsEnabled,
                !element.IsOffscreen || hasPhysicalIntersection,
                element.IsFocused,
                element.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AutomationException exception)
        {
            throw Map(exception);
        }
    }

    public Task ClickAsync(Guid sessionId, TestTarget target, CancellationToken cancellationToken) =>
        ExecuteAsync(() => automationEngine.ClickAsync(
            sessionId,
            target.ToAutomationLocatorSet(),
            cancellationToken));

    public Task FocusAsync(Guid sessionId, TestTarget target, CancellationToken cancellationToken) =>
        ExecuteAsync(() => automationEngine.FocusAsync(
            sessionId,
            target.ToAutomationLocatorSet(),
            cancellationToken));

    public Task InputAsync(
        Guid sessionId,
        TestTarget target,
        string text,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => automationEngine.TypeTextAsync(
            sessionId,
            target.ToAutomationLocatorSet(),
            text,
            cancellationToken));

    private static async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AutomationException exception)
        {
            throw Map(exception);
        }
    }

    private static AutomationDriverException Map(AutomationException exception) => exception switch
    {
        ElementNotFoundException => new("element_not_found", exception.Message, true, exception),
        ElementNotInteractableException => new("element_not_interactable", exception.Message, true, exception),
        AutomationTimeoutException => new("automation_timeout", exception.Message, true, exception),
        AutomationBackendException => new("automation_backend", exception.Message, true, exception),
        SessionNotFoundException => new("session_not_found", exception.Message, false, exception),
        ApplicationNotFoundException => new("application_not_found", exception.Message, false, exception),
        ApplicationAttachException => new("application_attach", exception.Message, false, exception),
        WindowNotFoundException => new("window_not_found", exception.Message, false, exception),
        _ => new("automation_error", exception.Message, true, exception)
    };

    private static bool Intersects(BoundingRectangle left, BoundingRectangle right) =>
        !left.IsEmpty &&
        !right.IsEmpty &&
        left.X < right.X + right.Width &&
        left.X + left.Width > right.X &&
        left.Y < right.Y + right.Height &&
        left.Y + left.Height > right.Y;
}

internal static class TestTargetMapper
{
    internal static LocatorSet ToAutomationLocatorSet(this TestTarget target) =>
        new(target.Locators.Select(locator => new ElementLocator(
            Enum.Parse<LocatorKind>(locator.Kind.ToString()),
            locator.Value,
            locator.Score)));
}
