using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QASentinel.Desktop.Services;
using QASentinel.Desktop.State;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class DashboardViewModel(
    AppState state,
    NavigationService navigation,
    ILogger<DashboardViewModel> logger) : ObservableObject
{
    public AppState State { get; } = state;

    [ObservableProperty]
    private string _lastAction = "Pronto para iniciar a validação no Test Playground.";

    [RelayCommand]
    private void OpenInspector() => navigation.Navigate("Inspector");

    [RelayCommand]
    private void OpenProjects() => navigation.Navigate("Projects");

    [RelayCommand]
    private void StartPlayground()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Playground", "QASentinel.TestPlayground.exe");
        if (!File.Exists(executable))
        {
            LastAction = "Test Playground não encontrado no diretório de saída.";
            return;
        }

        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        LastAction = "Test Playground iniciado. Abra o Inspector para conectar.";
        logger.LogInformation("Operation=LaunchPlayground ExecutablePath={ExecutablePath} Result=Success", executable);
    }
}
