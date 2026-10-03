using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QASentinel.Desktop.Services;
using QASentinel.Desktop.State;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly ProjectsViewModel _projects;
    private readonly InspectorViewModel _inspector;
    private readonly TestsViewModel _tests;
    private readonly ExecutionViewModel _execution;
    private readonly RecorderViewModel _recorder;
    private readonly SettingsViewModel _settings;

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        ProjectsViewModel projects,
        InspectorViewModel inspector,
        TestsViewModel tests,
        ExecutionViewModel execution,
        RecorderViewModel recorder,
        SettingsViewModel settings,
        NavigationService navigation,
        AppState state)
    {
        _dashboard = dashboard;
        _projects = projects;
        _inspector = inspector;
        _tests = tests;
        _execution = execution;
        _recorder = recorder;
        _settings = settings;
        State = state;
        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new("Dashboard", "⌂", "Dashboard"),
            new("Projects", "▣", "Projetos"),
            new("Tests", "✓", "Testes"),
            new("Execution", "▶", "Execução"),
            new("Recorder", "●", "Gravador"),
            new("Inspector", "⌕", "Inspector"),
            new("Api", "⇄", "Monitor de API", false),
            new("Reports", "▤", "Relatórios", false),
            new("Ai", "✦", "IA - Análise QA", false),
            new("Database", "◫", "Banco de Dados", false),
            new("Settings", "⚙", "Configurações")
        };

        navigation.NavigationRequested += (_, destination) => Navigate(destination);
        Navigate("Dashboard");
    }

    public AppState State { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _pageTitle = "Dashboard";

    [ObservableProperty]
    private string _pageSubtitle = "Visão geral da fundação de automação desktop.";

    [ObservableProperty]
    private string _selectedPageId = "Dashboard";

    [RelayCommand]
    private void Navigate(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        if (CurrentPage == _inspector && !string.Equals(destination, "Inspector", StringComparison.Ordinal))
        {
            _ = _inspector.StopInspectorAsync();
        }

        SelectedPageId = destination;
        (object Page, string Title, string Subtitle) selection = destination switch
        {
            "Dashboard" => (_dashboard, "Dashboard", "Status do projeto e acesso rápido às operações disponíveis."),
            "Projects" => (_projects, "Projetos", "Aplicações e contextos de QA persistidos localmente."),
            "Inspector" => (_inspector, "Inspector de Componentes", "Identifique e interaja com elementos em tempo real."),
            "Tests" => (_tests, "Casos de Teste", "Organize passos semânticos e locators persistentes."),
            "Execution" => (_execution, "Execução", "Execute pelo Agent e acompanhe assertions, retries e resultados."),
            "Recorder" => (_recorder, "Gravador de Testes", "Capture cliques e gere passos semânticos automaticamente."),
            "Settings" => (_settings, "Configurações", "Preferências locais do motor de automação."),
            "Api" => (new ComingSoonViewModel("Monitor de API"), "Monitor de API", "Fora do escopo do primeiro MVP."),
            "Reports" => (new ComingSoonViewModel("Relatórios"), "Relatórios", "Fora do escopo do primeiro MVP."),
            "Ai" => (new ComingSoonViewModel("IA - Análise QA"), "IA - Análise QA", "A IA será adicionada após o motor de automação."),
            "Database" => (new ComingSoonViewModel("Banco de Dados"), "Banco de Dados", "Fora do escopo do primeiro MVP."),
            _ => (_dashboard, "Dashboard", "Visão geral da fundação de automação desktop.")
        };
        CurrentPage = selection.Page;
        PageTitle = selection.Title;
        PageSubtitle = selection.Subtitle;

        if (destination == "Tests")
        {
            _ = _tests.LoadAsync();
        }
        else if (destination == "Execution")
        {
            _ = _execution.LoadAsync();
        }
        else if (destination == "Recorder")
        {
            _ = _recorder.LoadAsync();
        }
    }
}
