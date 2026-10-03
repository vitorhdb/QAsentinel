using QASentinel.Automation.Models;
using QASentinel.Automation.Win32;

namespace QASentinel.IntegrationTests;

public sealed class HighlightOverlayTests
{
    [Fact]
    [Trait("Category", "DesktopIntegration")]
    public async Task Overlay_CanShowMoveAndHideWithoutTakingTheCallingThread()
    {
        await using var highlighter = new WpfElementHighlighter();
        // O primeiro bootstrap WPF após atualização do runtime pode carregar assemblies nativos a frio.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await highlighter.ShowAsync(new BoundingRectangle(20, 20, 140, 44), timeout.Token);
        await highlighter.ShowAsync(new BoundingRectangle(40, 40, 160, 52), timeout.Token);
        await highlighter.HideAsync(timeout.Token);
    }
}
