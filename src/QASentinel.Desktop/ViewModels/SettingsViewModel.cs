using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using QASentinel.Infrastructure;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private int _operationTimeoutSeconds = 5;

    [ObservableProperty]
    private string _screenshotFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "QA Sentinel",
        "Screenshots");

    public string DatabasePath { get; } = DependencyInjection.GetDefaultDatabasePath();

    public string AutomationBackend => "UIA3 (UIA2 e Win32 preparados como fallbacks futuros)";
}
