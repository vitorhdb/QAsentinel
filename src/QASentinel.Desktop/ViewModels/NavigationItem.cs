namespace QASentinel.Desktop.ViewModels;

public sealed record NavigationItem(string Id, string Icon, string Title, bool IsAvailable = true)
{
    public string Status => IsAvailable ? string.Empty : "planejado";
}
