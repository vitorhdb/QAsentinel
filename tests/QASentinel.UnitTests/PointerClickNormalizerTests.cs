using QASentinel.Automation.Models;
using QASentinel.Recorder;

namespace QASentinel.UnitTests;

public sealed class PointerClickNormalizerTests
{
    [Fact]
    public void DownAndUpOnSameElementBecomeSemanticClick()
    {
        var normalizer = new PointerClickNormalizer();
        var now = DateTimeOffset.UtcNow;
        var element = CreateElement();

        var down = normalizer.Process(
            new RawPointerEvent(PointerEventKind.Down, PointerButton.Left, 100, 100, now),
            element);
        var up = normalizer.Process(
            new RawPointerEvent(PointerEventKind.Up, PointerButton.Left, 103, 102, now.AddMilliseconds(80)),
            null);

        Assert.Null(down);
        Assert.NotNull(up);
        Assert.Equal(RecordedActionType.Click, up.Action);
        Assert.Equal("btnSave", up.Element.AutomationId);
    }

    [Fact]
    public void MovementBeyondThresholdIsNotRecordedAsClick()
    {
        var normalizer = new PointerClickNormalizer(maximumMovement: 5);
        var now = DateTimeOffset.UtcNow;
        _ = normalizer.Process(
            new RawPointerEvent(PointerEventKind.Down, PointerButton.Left, 100, 100, now),
            CreateElement());

        var result = normalizer.Process(
            new RawPointerEvent(PointerEventKind.Up, PointerButton.Left, 130, 130, now.AddMilliseconds(100)),
            null);

        Assert.Null(result);
    }

    [Fact]
    public void DownWithoutResolvedElementIsIgnored()
    {
        var normalizer = new PointerClickNormalizer();
        var now = DateTimeOffset.UtcNow;
        _ = normalizer.Process(
            new RawPointerEvent(PointerEventKind.Down, PointerButton.Left, 100, 100, now),
            null);

        var result = normalizer.Process(
            new RawPointerEvent(PointerEventKind.Up, PointerButton.Left, 100, 100, now.AddMilliseconds(50)),
            null);

        Assert.Null(result);
    }

    private static AutomationElementInfo CreateElement() =>
        new(
            "Salvar",
            "btnSave",
            "Button",
            "Button",
            "WPF",
            null,
            new nint(123),
            true,
            false,
            false,
            new BoundingRectangle(80, 80, 80, 30),
            new LocatorSet(
            [
                new ElementLocator(LocatorKind.AutomationId, "btnSave"),
                new ElementLocator(LocatorKind.Name, "Salvar")
            ]),
            ["Invoke"]);
}
