using QASentinel.Domain.Testing;

namespace QASentinel.Domain.Entities;

public sealed class StepExecution
{
    private StepExecution()
    {
    }

    public StepExecution(
        Guid executionId,
        Guid testStepId,
        int sequence,
        TestActionType action,
        StepExecutionStatus status,
        int attempts,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? errorCode = null,
        string? message = null)
    {
        if (executionId == Guid.Empty || testStepId == Guid.Empty)
        {
            throw new ArgumentException("Execução e passo devem possuir identificadores válidos.");
        }

        if (sequence < 1 || attempts < 0 ||
            (attempts == 0 && status != StepExecutionStatus.Skipped) ||
            completedAt < startedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequência, tentativas ou período inválidos.");
        }

        Id = Guid.NewGuid();
        ExecutionId = executionId;
        TestStepId = testStepId;
        Sequence = sequence;
        Action = action;
        Status = status;
        Attempts = attempts;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        DurationMilliseconds = Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);
        ErrorCode = NormalizeOptional(errorCode);
        Message = NormalizeOptional(message);
    }

    public Guid Id { get; private set; }

    public Guid ExecutionId { get; private set; }

    public Guid TestStepId { get; private set; }

    public int Sequence { get; private set; }

    public TestActionType Action { get; private set; }

    public StepExecutionStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public long DurationMilliseconds { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? Message { get; private set; }

    public TestExecution Execution { get; private set; } = null!;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
