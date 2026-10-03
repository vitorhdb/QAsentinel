using QASentinel.Domain.Testing;

namespace QASentinel.Domain.Entities;

public sealed class TestCase
{
    private readonly List<TestStep> _steps = [];

    private TestCase()
    {
    }

    public TestCase(Guid projectId, string name, string? description = null)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("O projeto do caso de teste é obrigatório.", nameof(projectId));
        }

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Rename(name);
        Description = NormalizeOptional(description);
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? Preconditions { get; private set; }

    public TestCasePriority Priority { get; private set; } = TestCasePriority.Medium;

    public string? Tags { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Project Project { get; private set; } = null!;

    public IReadOnlyCollection<TestStep> Steps => _steps.AsReadOnly();

    public TestStep AddStep(
        TestActionType action,
        TestTarget target,
        string? input = null,
        TimeSpan? timeout = null,
        int maxAttempts = 1)
    {
        var step = new TestStep(
            Id,
            _steps.Count + 1,
            action,
            target,
            input,
            timeout ?? TimeSpan.FromSeconds(5),
            maxAttempts);
        _steps.Add(step);
        Touch();
        return step;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Touch();
    }

    public void Configure(
        string? description,
        string? preconditions,
        TestCasePriority priority,
        string? tags,
        bool isEnabled)
    {
        Description = NormalizeOptional(description);
        Preconditions = NormalizeOptional(preconditions);
        Priority = priority;
        Tags = NormalizeOptional(tags);
        IsEnabled = isEnabled;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
