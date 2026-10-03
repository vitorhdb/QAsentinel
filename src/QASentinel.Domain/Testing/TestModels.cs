namespace QASentinel.Domain.Testing;

public enum TestActionType
{
    Click,
    Focus,
    Input,
    WaitForElement,
    AssertExists,
    AssertVisible,
    AssertEnabled
}

public enum TestCasePriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum TestExecutionStatus
{
    Running,
    Passed,
    Failed,
    Error,
    Cancelled
}

public enum StepExecutionStatus
{
    Running,
    Passed,
    Failed,
    Error,
    Cancelled,
    Skipped
}

public enum TestLocatorKind
{
    AutomationId,
    Name,
    ClassName,
    ControlType,
    NativeWindowHandle,
    Parent,
    Index,
    Coordinates
}

public sealed record TestLocator
{
    public TestLocator(TestLocatorKind kind, string value, int score)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (score is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(score), "O score do locator deve estar entre 0 e 100.");
        }

        Kind = kind;
        Value = value.Trim();
        Score = score;
    }

    public TestLocatorKind Kind { get; }

    public string Value { get; }

    public int Score { get; }
}

public sealed class TestTarget
{
    public TestTarget(IReadOnlyList<TestLocator> locators)
    {
        ArgumentNullException.ThrowIfNull(locators);
        Locators = locators
            .Where(locator => locator is not null)
            .GroupBy(locator => new { locator.Kind, locator.Value })
            .Select(group => group.OrderByDescending(locator => locator.Score).First())
            .OrderByDescending(locator => locator.Score)
            .ToArray();

        if (Locators.Count == 0)
        {
            throw new ArgumentException("O alvo do passo deve possuir ao menos um locator.", nameof(locators));
        }
    }

    public IReadOnlyList<TestLocator> Locators { get; }
}
