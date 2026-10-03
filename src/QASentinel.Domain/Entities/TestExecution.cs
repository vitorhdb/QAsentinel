using QASentinel.Domain.Testing;

namespace QASentinel.Domain.Entities;

public sealed class TestExecution
{
    private readonly List<StepExecution> _steps = [];

    private TestExecution()
    {
    }

    public TestExecution(
        Guid id,
        Guid testCaseId,
        Guid sessionId,
        string machineName,
        string? applicationVersion,
        DateTimeOffset startedAt)
    {
        if (id == Guid.Empty || testCaseId == Guid.Empty || sessionId == Guid.Empty)
        {
            throw new ArgumentException("Execução, caso de teste e sessão devem possuir identificadores válidos.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        Id = id;
        TestCaseId = testCaseId;
        SessionId = sessionId;
        MachineName = machineName.Trim();
        ApplicationVersion = string.IsNullOrWhiteSpace(applicationVersion) ? null : applicationVersion.Trim();
        StartedAt = startedAt;
        Status = TestExecutionStatus.Running;
    }

    public Guid Id { get; private set; }

    public Guid TestCaseId { get; private set; }

    public Guid SessionId { get; private set; }

    public TestExecutionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public long DurationMilliseconds { get; private set; }

    public string MachineName { get; private set; } = string.Empty;

    public string? ApplicationVersion { get; private set; }

    public string? ErrorMessage { get; private set; }

    public TestCase TestCase { get; private set; } = null!;

    public IReadOnlyCollection<StepExecution> Steps => _steps.AsReadOnly();

    public void AddStep(StepExecution step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.ExecutionId != Id)
        {
            throw new InvalidOperationException("O resultado do passo pertence a outra execução.");
        }

        if (_steps.Any(existing => existing.Sequence == step.Sequence))
        {
            throw new InvalidOperationException($"Já existe resultado para o passo {step.Sequence}.");
        }

        _steps.Add(step);
    }

    public void Complete(TestExecutionStatus status, DateTimeOffset completedAt, string? errorMessage = null)
    {
        if (status == TestExecutionStatus.Running)
        {
            throw new ArgumentException("A execução não pode ser finalizada com status Running.", nameof(status));
        }

        if (completedAt < StartedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAt));
        }

        Status = status;
        CompletedAt = completedAt;
        DurationMilliseconds = Math.Max(0, (long)(completedAt - StartedAt).TotalMilliseconds);
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
    }
}
