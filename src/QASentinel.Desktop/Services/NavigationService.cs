namespace QASentinel.Desktop.Services;

public sealed class NavigationService
{
    public event EventHandler<string>? NavigationRequested;

    public void Navigate(string destination) => NavigationRequested?.Invoke(this, destination);
}
