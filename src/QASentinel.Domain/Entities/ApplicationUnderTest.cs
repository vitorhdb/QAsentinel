namespace QASentinel.Domain.Entities;

public sealed class ApplicationUnderTest
{
    private ApplicationUnderTest()
    {
    }

    public ApplicationUnderTest(Guid projectId, string name, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Name = name.Trim();
        ExecutablePath = executablePath.Trim();
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string ExecutablePath { get; private set; } = string.Empty;

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    public string PreferredBackend { get; set; } = "UIA3";

    public Project Project { get; private set; } = null!;
}
