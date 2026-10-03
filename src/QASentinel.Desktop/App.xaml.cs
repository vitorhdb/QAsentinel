using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Agents;
using QASentinel.Desktop.Services;
using QASentinel.Desktop.State;
using QASentinel.Desktop.ViewModels;
using QASentinel.Infrastructure;
using QASentinel.Infrastructure.Persistence;
using QASentinel.Observability;

namespace QASentinel.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.AddQASentinelObservability("Desktop");
            builder.Services.AddInfrastructure();
            builder.Services.AddSingleton<AppState>();
            builder.Services.AddSingleton<TestDraftState>();
            builder.Services.AddSingleton<IAgentClient, NamedPipeAgentClient>();
            builder.Services.AddSingleton<AgentProcessManager>();
            builder.Services.AddSingleton<AgentConnectionService>();
            builder.Services.AddSingleton<NavigationService>();
            builder.Services.AddSingleton<DashboardViewModel>();
            builder.Services.AddSingleton<ProjectsViewModel>();
            builder.Services.AddSingleton<InspectorViewModel>();
            builder.Services.AddSingleton<TestsViewModel>();
            builder.Services.AddSingleton<ExecutionViewModel>();
            builder.Services.AddSingleton<RecorderViewModel>();
            builder.Services.AddSingleton<SettingsViewModel>();
            builder.Services.AddSingleton<MainWindowViewModel>();
            builder.Services.AddSingleton<MainWindow>();

            _host = builder.Build();
            await _host.StartAsync().ConfigureAwait(true);
            await _host.Services.GetRequiredService<DatabaseInitializer>()
                .InitializeAsync(CancellationToken.None)
                .ConfigureAwait(true);
            await _host.Services.GetRequiredService<AgentConnectionService>()
                .InitializeAsync(CancellationToken.None)
                .ConfigureAwait(true);

            _host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception exception)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "Operation=DesktopStartup Result=Failure");
            MessageBox.Show(
                $"O QA Sentinel não pôde ser iniciado.\n\n{exception.Message}",
                "QA Sentinel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.Services.GetRequiredService<InspectorViewModel>().ShutdownAsync().ConfigureAwait(true);
            await _host.Services.GetRequiredService<RecorderViewModel>().ShutdownAsync().ConfigureAwait(true);
            await _host.Services.GetRequiredService<IAgentClient>().DisposeAsync().ConfigureAwait(true);
            await _host.Services.GetRequiredService<AgentProcessManager>().DisposeAsync().ConfigureAwait(true);
            await _host.StopAsync().ConfigureAwait(true);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
