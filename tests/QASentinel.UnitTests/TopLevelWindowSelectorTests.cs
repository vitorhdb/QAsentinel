using QASentinel.Automation.Models;

namespace QASentinel.UnitTests;

public sealed class TopLevelWindowSelectorTests
{
    [Fact]
    public void SelectBest_PrefersVisibleDelphiFormOverHiddenApplicationWindow()
    {
        var hiddenApplicationWindow = new TopLevelWindowDescriptor(
            new nint(10),
            string.Empty,
            "TApplication",
            IsVisible: false,
            IsEnabled: true,
            IsToolWindow: false,
            OwnerHandle: nint.Zero,
            Bounds: default);
        var visibleForm = new TopLevelWindowDescriptor(
            new nint(20),
            "Argus",
            "TFrmPrincipal",
            IsVisible: true,
            IsEnabled: true,
            IsToolWindow: false,
            OwnerHandle: hiddenApplicationWindow.Handle,
            Bounds: new BoundingRectangle(100, 100, 1280, 720));

        var selected = TopLevelWindowSelector.SelectBest(
            [hiddenApplicationWindow, visibleForm],
            hiddenApplicationWindow.Handle);

        Assert.Same(visibleForm, selected);
    }
}
