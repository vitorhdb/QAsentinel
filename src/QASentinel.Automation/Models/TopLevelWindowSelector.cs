namespace QASentinel.Automation.Models;

public static class TopLevelWindowSelector
{
    public static TopLevelWindowDescriptor? SelectBest(
        IEnumerable<TopLevelWindowDescriptor> windows,
        nint processMainWindowHandle = default)
    {
        ArgumentNullException.ThrowIfNull(windows);
        return windows
            .Where(window => window.Handle != nint.Zero)
            .OrderByDescending(window => Score(window, processMainWindowHandle))
            .ThenByDescending(window => window.Bounds.Width * window.Bounds.Height)
            .FirstOrDefault();
    }

    private static int Score(TopLevelWindowDescriptor window, nint processMainWindowHandle)
    {
        var score = 0;
        score += window.IsVisible ? 10_000 : 0;
        score += !window.Bounds.IsEmpty ? 5_000 : 0;
        score += !string.Equals(window.ClassName, "TApplication", StringComparison.OrdinalIgnoreCase) ? 3_000 : -3_000;
        score += !window.IsToolWindow ? 1_000 : -1_000;
        score += window.IsEnabled ? 500 : 0;
        score += !string.IsNullOrWhiteSpace(window.Title) ? 250 : 0;
        score += window.OwnerHandle == nint.Zero ? 100 : 0;
        score += window.Handle == processMainWindowHandle ? 50 : 0;
        return score;
    }
}
