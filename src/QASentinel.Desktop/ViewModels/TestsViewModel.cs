using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Projects;
using QASentinel.Application.Tests;
using QASentinel.Desktop.State;
using QASentinel.Domain.Entities;
using QASentinel.Domain.Testing;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class TestsViewModel(
    IProjectRepository projectRepository,
    ITestCaseRepository testCaseRepository,
    TestDraftState draft,
    ILogger<TestsViewModel> logger) : ObservableObject
{
    public ObservableCollection<Project> Projects { get; } = [];

    public ObservableCollection<TestCase> TestCases { get; } = [];

    public TestDraftState Draft { get; } = draft;

    [ObservableProperty]
    private Project? _selectedProject;

    [ObservableProperty]
    private TestCase? _selectedTestCase;

    [ObservableProperty]
    private string _testName = "Novo caso de teste";

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Capture elementos no Inspector e adicione-os ao rascunho.";

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var projects = await projectRepository.GetAllAsync(timeout.Token);
            var testCases = await testCaseRepository.GetAllAsync(timeout.Token);
            Projects.Clear();
            foreach (var project in projects)
            {
                Projects.Add(project);
            }

            SelectedProject ??= Projects.FirstOrDefault();
            TestCases.Clear();
            foreach (var testCase in testCases)
            {
                TestCases.Add(testCase);
            }

            StatusMessage = $"{TestCases.Count} caso(s) salvo(s); {Draft.Steps.Count} passo(s) no rascunho.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=LoadTestCases Result=Failure");
            StatusMessage = "Não foi possível carregar os casos de teste.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Crie ou selecione um projeto antes de salvar o caso.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TestName))
        {
            StatusMessage = "Informe o nome do caso de teste.";
            return;
        }

        if (Draft.Steps.Count == 0)
        {
            StatusMessage = "Adicione ao menos um passo pelo Inspector.";
            return;
        }

        IsBusy = true;
        try
        {
            var testCase = new TestCase(SelectedProject.Id, TestName, Description);
            foreach (var draftStep in Draft.Steps.OrderBy(step => step.Sequence))
            {
                testCase.AddStep(
                    Enum.Parse<TestActionType>(draftStep.Action.ToString()),
                    new TestTarget(draftStep.Target.Locators.Select(locator => new TestLocator(
                        Enum.Parse<TestLocatorKind>(locator.Kind.ToString()),
                        locator.Value,
                        locator.Score)).ToArray()),
                    draftStep.Input,
                    TimeSpan.FromMilliseconds(draftStep.TimeoutMilliseconds),
                    draftStep.MaxAttempts);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await testCaseRepository.AddAsync(testCase, timeout.Token);
            TestCases.Add(testCase);
            SelectedTestCase = testCase;
            Draft.Clear();
            TestName = "Novo caso de teste";
            Description = string.Empty;
            StatusMessage = "Caso salvo no SQLite e disponível para execução.";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=SaveTestCase Result=Failure");
            StatusMessage = $"Não foi possível salvar o caso: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearDraft()
    {
        Draft.Clear();
        StatusMessage = "Rascunho limpo.";
    }
}
