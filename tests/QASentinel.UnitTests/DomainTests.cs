using QASentinel.Domain.Entities;

namespace QASentinel.UnitTests;

public sealed class DomainTests
{
    [Fact]
    public void Project_NormalizesItsNameAndCreatesIdentity()
    {
        var project = new Project("  Argus Desktop  ", "Automação Delphi");

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("Argus Desktop", project.Name);
        Assert.True(project.CreatedAt <= DateTimeOffset.UtcNow);
    }
}
