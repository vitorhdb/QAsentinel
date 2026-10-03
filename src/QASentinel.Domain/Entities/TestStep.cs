using QASentinel.Domain.Testing;

namespace QASentinel.Domain.Entities;

public sealed class TestStep
{
    private TestStep()
    {
        Target = null!;
    }

    internal TestStep(
        Guid testCaseId,
        int sequence,
        TestActionType action,
        TestTarget target,
        string? input,
        TimeSpan timeout,
        int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (timeout < TimeSpan.FromMilliseconds(100) || timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "O timeout deve estar entre 100 ms e 2 minutos.");
        }

        if (maxAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "A quantidade de tentativas deve estar entre 1 e 10.");
        }

        if (action == TestActionType.Input && input is null)
        {
            throw new ArgumentException("Passos de entrada precisam informar o texto.", nameof(input));
        }

        Id = Guid.NewGuid();
        TestCaseId = testCaseId;
        Sequence = sequence;
        Action = action;
        Target = target;
        Input = input;
        TimeoutMilliseconds = checked((int)timeout.TotalMilliseconds);
        MaxAttempts = maxAttempts;
    }

    public Guid Id { get; private set; }

    public Guid TestCaseId { get; private set; }

    public int Sequence { get; private set; }

    public TestActionType Action { get; private set; }

    public TestTarget Target { get; private set; }

    public string? Input { get; private set; }

    public int TimeoutMilliseconds { get; private set; }

    public int MaxAttempts { get; private set; }

    public TestCase TestCase { get; private set; } = null!;
}
