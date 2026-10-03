namespace QASentinel.Domain.Entities;

public sealed class Project
{
    private Project()
    {
    }

    public Project(string name, string? description = null)
    {
        Id = Guid.NewGuid();
        Rename(name);
        Description = description?.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public ICollection<ApplicationUnderTest> Applications { get; } = new List<ApplicationUnderTest>();

    public ICollection<TestCase> TestCases { get; } = new List<TestCase>();

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }
}
