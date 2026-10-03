using QASentinel.Automation.Models;

namespace QASentinel.UnitTests;

public sealed class LocatorSetTests
{
    [Fact]
    public void Constructor_OrdersLocatorsByReliabilityAndDropsEmptyValues()
    {
        var set = new LocatorSet(
        [
            new ElementLocator(LocatorKind.Coordinates, "10,20"),
            new ElementLocator(LocatorKind.Name, "Salvar"),
            new ElementLocator(LocatorKind.AutomationId, "btnSave"),
            new ElementLocator(LocatorKind.ClassName, "Button"),
            new ElementLocator(LocatorKind.Parent, string.Empty)
        ]);

        Assert.Equal(LocatorKind.AutomationId, set.Best?.Kind);
        Assert.Equal(LocatorKind.Coordinates, set.Locators[^1].Kind);
        Assert.DoesNotContain(set.Locators, locator => string.IsNullOrWhiteSpace(locator.Value));
    }

    [Fact]
    public void Constructor_DeduplicatesSameKindAndValueKeepingHighestScore()
    {
        var set = new LocatorSet(
        [
            new ElementLocator(LocatorKind.Name, " Salvar ", 20),
            new ElementLocator(LocatorKind.Name, "Salvar", 85)
        ]);

        var locator = Assert.Single(set.Locators);
        Assert.Equal("Salvar", locator.Value);
        Assert.Equal(85, locator.Score);
    }
}
