using QASentinel.Domain.Testing;

namespace QASentinel.Application.Executions;

public sealed record TestCaseDefinition(
    Guid TestCaseId,
    string Name,
    IReadOnlyList<TestStepDefinition> Steps);

public sealed record TestStepDefinition(
    Guid StepId,
    int Sequence,
    TestActionType Action,
    TestTarget Target,
    string? Input,
    TimeSpan Timeout,
    int MaxAttempts);

public sealed record AutomationElementState(
    bool Exists,
    bool IsEnabled,
    bool IsVisible,
    bool IsFocused,
    string? Name);

public sealed record StepRunResult(
    Guid StepId,
    int Sequence,
    TestActionType Action,
    StepExecutionStatus Status,
    int Attempts,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? ErrorCode = null,
    string? Message = null)
{
    public long DurationMilliseconds => Math.Max(0, (long)(CompletedAt - StartedAt).TotalMilliseconds);
}

public sealed record TestRunResult(
    Guid ExecutionId,
    Guid TestCaseId,
    Guid SessionId,
    TestExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<StepRunResult> Steps,
    string? ErrorMessage = null)
{
    public long DurationMilliseconds => Math.Max(0, (long)(CompletedAt - StartedAt).TotalMilliseconds);
}

public sealed class AutomationDriverException(
    string code,
    string message,
    bool isTransient = true,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool IsTransient { get; } = isTransient;
}
