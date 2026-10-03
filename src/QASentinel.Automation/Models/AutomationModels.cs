namespace QASentinel.Automation.Models;

public enum AutomationBackend
{
    Uia3,
    Uia2,
    Win32
}

public enum WindowPresentationState
{
    Normal,
    Minimized,
    Maximized
}

public enum LocatorKind
{
    AutomationId,
    Name,
    ClassName,
    ControlType,
    NativeWindowHandle,
    Parent,
    Index,
    Coordinates
}

public readonly record struct BoundingRectangle(double X, double Y, double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double CenterX => X + (Width / 2d);

    public double CenterY => Y + (Height / 2d);
}

public sealed record ElementLocator(LocatorKind Kind, string Value, int Score)
{
    public ElementLocator(LocatorKind kind, string value)
        : this(kind, value, LocatorScores.For(kind))
    {
    }
}

public static class LocatorScores
{
    public static int For(LocatorKind kind) => kind switch
    {
        LocatorKind.AutomationId => 100,
        LocatorKind.NativeWindowHandle => 90,
        LocatorKind.Name => 85,
        LocatorKind.Parent => 80,
        LocatorKind.ClassName => 70,
        LocatorKind.ControlType => 65,
        LocatorKind.Index => 50,
        LocatorKind.Coordinates => 10,
        _ => 0
    };
}

public sealed class LocatorSet
{
    private readonly IReadOnlyList<ElementLocator> _locators;

    public LocatorSet(IEnumerable<ElementLocator> locators)
    {
        ArgumentNullException.ThrowIfNull(locators);
        _locators = locators
            .Where(locator => !string.IsNullOrWhiteSpace(locator.Value))
            .GroupBy(locator => new { locator.Kind, Value = locator.Value.Trim() })
            .Select(group => group.OrderByDescending(locator => locator.Score).First() with { Value = group.Key.Value })
            .OrderByDescending(locator => locator.Score)
            .ToArray();
    }

    public IReadOnlyList<ElementLocator> Locators => _locators;

    public ElementLocator? Best => _locators.FirstOrDefault();
}

public sealed record AutomationElementInfo(
    string? Name,
    string? AutomationId,
    string? ClassName,
    string? ControlType,
    string? FrameworkId,
    string? HelpText,
    nint NativeWindowHandle,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsFocused,
    BoundingRectangle Bounds,
    LocatorSet Locators,
    IReadOnlyList<string> SupportedPatterns);

public sealed record AutomationValueInfo(
    bool IsSupported,
    bool IsReadOnly,
    bool IsPassword,
    string? Value);

public sealed record CompatibilityScanOptions(int MaxDepth = 12, int MaxElements = 2_000)
{
    public CompatibilityScanOptions Validate()
    {
        if (MaxDepth is < 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDepth), "A profundidade deve estar entre 0 e 64.");
        }

        if (MaxElements is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxElements), "A quantidade de elementos deve estar entre 1 e 10.000.");
        }

        return this;
    }
}

public sealed record AutomationTreeElementInfo(
    int Sequence,
    int Depth,
    int? ParentSequence,
    int SiblingIndex,
    AutomationElementInfo Element);

public sealed record AutomationCompatibilityScan(
    ApplicationSession Session,
    DateTimeOffset CapturedAt,
    IReadOnlyList<AutomationTreeElementInfo> Elements,
    bool ElementLimitReached,
    IReadOnlyList<string> Warnings);

public sealed record ApplicationSession(
    Guid Id,
    int ProcessId,
    string? ExecutablePath,
    string ProcessName,
    nint MainWindowHandle,
    string WindowTitle,
    DateTimeOffset StartedAt,
    AutomationBackend Backend);

public sealed record ProcessDescriptor(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    string? ExecutablePath,
    string Architecture,
    nint MainWindowHandle);

public sealed record TopLevelWindowDescriptor(
    nint Handle,
    string Title,
    string ClassName,
    bool IsVisible,
    bool IsEnabled,
    bool IsToolWindow,
    nint OwnerHandle,
    BoundingRectangle Bounds);

public enum ScreenshotTarget
{
    FullScreen,
    Application,
    Element
}

public sealed record ScreenshotResult(string FilePath, int Width, int Height, DateTimeOffset CapturedAt);
