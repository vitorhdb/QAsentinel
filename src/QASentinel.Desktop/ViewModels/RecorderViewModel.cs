using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Agents;
using QASentinel.Application.Projects;
using QASentinel.Application.Tests;
using QASentinel.Contracts;
using QASentinel.Desktop.Services;
using QASentinel.Desktop.State;
using QASentinel.Domain.Entities;
using QASentinel.Domain.Testing;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class RecorderViewModel(
    IAgentClient agentClient,
    IProjectRepository projectRepository,
    ITestCaseRepository testCaseRepository,
    NavigationService navigation,
    AppState state,
    ILogger<RecorderViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;

    public AppState State { get; } = state;

    public ObservableCollection<Project> Projects { get; } = [];

    public ObservableCollection<RecordedStepDto> Steps { get; } = [];

    [ObservableProperty]
    private Project? _selectedProject;

    [ObservableProperty]
    private string _testName = "Cenário gravado";

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Conecte uma aplicação no Inspector para iniciar a gravação.";

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var projects = await projectRepository.GetAllAsync(timeout.Token);
            Projects.Clear();
            foreach (var project in projects)
            {
                Projects.Add(project);
            }

            SelectedProject ??= Projects.FirstOrDefault();
            if (agentClient.IsConnected)
            {
                ApplySnapshot(await agentClient.GetRecordingAsync(timeout.Token));
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=LoadRecorder Result=Failure");
            StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy || IsRecording)
        {
            return;
        }

        if (State.Session is null)
        {
            StatusMessage = "Conecte uma aplicação no Inspector antes de gravar.";
            return;
        }

        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var snapshot = await agentClient.StartRecordingAsync(
                new StartRecordingRequest(State.Session.SessionId),
                timeout.Token);
            ApplySnapshot(snapshot);
            _pollCancellation = new CancellationTokenSource();
            _pollTask = PollAsync(_pollCancellation.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=StartRecorderUi Result=Failure");
            StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (State.Session is null || (!IsRecording && _pollTask is null))
        {
            return;
        }

        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var snapshot = await agentClient.StopRecordingAsync(
                new StopRecordingRequest(State.Session.SessionId),
                timeout.Token);
            ApplySnapshot(snapshot);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=StopRecorderUi Result=Failure");
            StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
        }
        finally
        {
            await StopPollingAsync();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SaveTestAsync() => SaveTestCoreAsync(openExecution: false);

    [RelayCommand]
    private Task SaveAndOpenExecutionAsync() => SaveTestCoreAsync(openExecution: true);

    private async Task SaveTestCoreAsync(bool openExecution)
    {
        if (IsRecording)
        {
            StatusMessage = "Pare a gravação antes de salvar o caso.";
            return;
        }

        if (SelectedProject is null)
        {
            StatusMessage = "Selecione um projeto para o caso gravado.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TestName) || Steps.Count == 0)
        {
            StatusMessage = "Informe o nome e capture ao menos um passo.";
            return;
        }

        IsBusy = true;
        try
        {
            var testCase = new TestCase(
                SelectedProject.Id,
                TestName,
                "Gerado pelo Recorder M3 a partir de interações do usuário.");
            foreach (var step in Steps.OrderBy(step => step.Sequence))
            {
                var action = step.Action switch
                {
                    RecordedActionDto.Click => TestActionType.Click,
                    RecordedActionDto.Fill => TestActionType.Input,
                    _ => throw new InvalidOperationException($"Ação gravada não suportada: {step.Action}.")
                };
                testCase.AddStep(
                    action,
                    new TestTarget(step.Target.Locators.Select(locator => new TestLocator(
                        Enum.Parse<TestLocatorKind>(locator.Kind.ToString()),
                        locator.Value,
                        locator.Score)).ToArray()),
                    step.Input,
                    timeout: TimeSpan.FromSeconds(5),
                    maxAttempts: 3);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await testCaseRepository.AddAsync(testCase, timeout.Token);
            StatusMessage = $"Caso “{testCase.Name}” salvo com {testCase.Steps.Count} passo(s).";
            if (openExecution)
            {
                navigation.Navigate("Execution");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=SaveRecordedTest Result=Failure");
            StatusMessage = $"Não foi possível salvar o caso gravado: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                ApplySnapshot(await agentClient.GetRecordingAsync(cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=PollRecorder Result=Failure");
            StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
            IsRecording = false;
        }
    }

    private void ApplySnapshot(RecorderStateResponse snapshot)
    {
        IsRecording = snapshot.IsRecording;
        StatusMessage = snapshot.StatusMessage;
        Steps.Clear();
        foreach (var step in snapshot.Steps.OrderBy(step => step.Sequence))
        {
            Steps.Add(step);
        }
    }

    private async Task StopPollingAsync()
    {
        _pollCancellation?.Cancel();
        if (_pollTask is not null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _pollCancellation?.Dispose();
        _pollCancellation = null;
        _pollTask = null;
    }

    public async Task ShutdownAsync()
    {
        if (IsRecording && State.Session is not null && agentClient.IsConnected)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _ = await agentClient.StopRecordingAsync(
                    new StopRecordingRequest(State.Session.SessionId),
                    timeout.Token);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Operation=ShutdownRecorder Result=Failure");
            }
        }

        await StopPollingAsync();
    }
}
