using QASentinel.Automation.Models;

namespace QASentinel.UnitTests;

public sealed class CompatibilityScanOptionsTests
{
    [Theory]
    [InlineData(-1, 100)]
    [InlineData(65, 100)]
    [InlineData(10, 0)]
    [InlineData(10, 10_001)]
    public void Validate_RejectsUnsafeLimits(int maxDepth, int maxElements)
    {
        var options = new CompatibilityScanOptions(maxDepth, maxElements);

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ReturnsValidatedOptions()
    {
        var options = new CompatibilityScanOptions(12, 2_000);

        Assert.Same(options, options.Validate());
    }
}
