using CommunityToolkit.Mvvm.ComponentModel;
using QASentinel.Contracts;

namespace QASentinel.Desktop.State;

public sealed partial class AppState : ObservableObject
{
    [ObservableProperty]
    private bool _isAgentConnected;

    [ObservableProperty]
    private string _connectionStatus = "Inicializando Agent";

    [ObservableProperty]
    private ApplicationSessionDto? _session;

    [ObservableProperty]
    private AutomationElementInfoDto? _currentElement;

    [ObservableProperty]
    private string? _lastScreenshotPath;

    public bool IsApplicationConnected => Session is not null;

    partial void OnSessionChanged(ApplicationSessionDto? value) => OnPropertyChanged(nameof(IsApplicationConnected));
}
