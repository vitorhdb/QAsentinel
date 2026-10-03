using QASentinel.Domain.Testing;

namespace QASentinel.Application.Executions;

public interface ITestAutomationDriver
{
    Task<AutomationElementState?> FindAsync(
        Guid sessionId,
        TestTarget target,
        CancellationToken cancellationToken);

    Task ClickAsync(Guid sessionId, TestTarget target, CancellationToken cancellationToken);

    Task FocusAsync(Guid sessionId, TestTarget target, CancellationToken cancellationToken);

    Task InputAsync(Guid sessionId, TestTarget target, string text, CancellationToken cancellationToken);
}

public interface ITestRunner
{
    Task<TestRunResult> RunAsync(
        Guid executionId,
        Guid sessionId,
        TestCaseDefinition testCase,
        Action<StepRunResult>? onStepCompleted,
        CancellationToken cancellationToken);
}

public interface IExecutionDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemExecutionDelay : IExecutionDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
