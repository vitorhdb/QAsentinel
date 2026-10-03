using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Agents;
using QASentinel.Application.Executions;
using QASentinel.Application.Tests;
using QASentinel.Contracts;
using QASentinel.Desktop.State;
using QASentinel.Domain.Entities;
using QASentinel.Domain.Testing;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class ExecutionViewModel(
    ITestCaseRepository testCaseRepository,
    ITestExecutionRepository executionRepository,
    IAgentClient agentClient,
    AppState state,
    ILogger<ExecutionViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? _executionCancellation;

    public AppState State { get; } = state;

    public ObservableCollection<TestCase> TestCases { get; } = [];

    public ObservableCollection<StepExecutionResultDto> CurrentSteps { get; } = [];

    public ObservableCollection<TestExecution> RecentExecutions { get; } = [];

    [ObservableProperty]
    private TestCase? _selectedTestCase;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private Guid? _currentExecutionId;

    [ObservableProperty]
    private string _executionStatus = "Pronto";

    [ObservableProperty]
    private string _statusMessage = "Selecione um caso e conecte uma aplicação no Inspector.";

    [ObservableProperty]
    private double _progressPercentage;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var testCases = await testCaseRepository.GetAllAsync(timeout.Token);
            var executions = await executionRepository.GetRecentAsync(50, timeout.Token);
            TestCases.Clear();
            foreach (var testCase in testCases)
            {
                TestCases.Add(testCase);
            }

            SelectedTestCase ??= TestCases.FirstOrDefault();
            RecentExecutions.Clear();
            foreach (var execution in executions)
            {
                RecentExecutions.Add(execution);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=LoadExecutions Result=Failure");
            StatusMessage = "Não foi possível carregar testes e execuções.";
        }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (State.Session is null)
        {
            StatusMessage = "Conecte a aplicação alvo no Inspector antes de executar.";
            return;
        }

        if (SelectedTestCase is null)
        {
            StatusMessage = "Selecione um caso de teste.";
            return;
        }

        if (SelectedTestCase.Steps.Count == 0)
        {
            StatusMessage = "O caso selecionado não possui passos.";
            return;
        }

        IsRunning = true;
        CurrentSteps.Clear();
        ProgressPercentage = 0;
        ExecutionStatus = "Running";
        _executionCancellation = new CancellationTokenSource();
        var executionId = Guid.NewGuid();
        CurrentExecutionId = executionId;

        try
        {
            using (var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_executionCancellation.Token))
            {
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                _ = await agentClient.StartExecutionAsync(
                    CreateRequest(executionId, State.Session.SessionId, SelectedTestCase),
                    requestTimeout.Token);
            }

            StatusMessage = "Execução iniciada no Agent.";
            TestExecutionStateResponse snapshot;
            do
            {
                await Task.Delay(250, _executionCancellation.Token);
                using var pollTimeout = CancellationTokenSource.CreateLinkedTokenSource(_executionCancellation.Token);
                pollTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                snapshot = await agentClient.GetExecutionAsync(
                    new GetTestExecutionRequest(executionId),
                    pollTimeout.Token);
                ApplySnapshot(snapshot, SelectedTestCase.Steps.Count);
            }
            while (snapshot.Status == TestExecutionStatusDto.Running);

            await PersistAsync(snapshot, _executionCancellation.Token);
            StatusMessage = snapshot.Status switch
            {
                TestExecutionStatusDto.Passed => "Execução concluída com sucesso.",
                TestExecutionStatusDto.Failed => "Uma assertion não foi atendida.",
                TestExecutionStatusDto.Error => "A automação não conseguiu concluir o teste.",
                TestExecutionStatusDto.Cancelled => "Execução cancelada.",
                _ => "Execução concluída."
            };
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Execução cancelada.";
            ExecutionStatus = "Cancelled";
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=RunTestFromDesktop ExecutionId={ExecutionId} Result=Failure", executionId);
            StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
            ExecutionStatus = "Error";
        }
        finally
        {
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!IsRunning || CurrentExecutionId is not { } executionId)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _ = await agentClient.CancelExecutionAsync(new CancelTestExecutionRequest(executionId), timeout.Token);
            StatusMessage = "Cancelamento solicitado ao Agent.";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Operation=CancelTestFromDesktop ExecutionId={ExecutionId} Result=Failure", executionId);
            StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
        }
    }

    private void ApplySnapshot(TestExecutionStateResponse snapshot, int totalSteps)
    {
        ExecutionStatus = snapshot.Status.ToString();
        CurrentSteps.Clear();
        foreach (var step in snapshot.Steps.OrderBy(step => step.Sequence))
        {
            CurrentSteps.Add(step);
        }

        var completed = snapshot.Steps.Count(step => step.Status != StepExecutionStatusDto.Running);
        ProgressPercentage = totalSteps == 0 ? 0 : Math.Clamp(completed * 100d / totalSteps, 0, 100);
    }

    private async Task PersistAsync(TestExecutionStateResponse snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.CompletedAt is not { } completedAt)
        {
            throw new InvalidOperationException("O Agent finalizou a execução sem informar o horário de conclusão.");
        }

        var execution = new TestExecution(
            snapshot.ExecutionId,
            snapshot.TestCaseId,
            snapshot.SessionId,
            snapshot.MachineName,
            snapshot.ApplicationVersion,
            snapshot.StartedAt);
        foreach (var step in snapshot.Steps.OrderBy(step => step.Sequence))
        {
            execution.AddStep(new StepExecution(
                execution.Id,
                step.StepId,
                step.Sequence,
                Enum.Parse<TestActionType>(step.Action.ToString()),
                Enum.Parse<StepExecutionStatus>(step.Status.ToString()),
                step.Attempts,
                step.StartedAt,
                step.CompletedAt,
                step.ErrorCode,
                step.Message));
        }

        execution.Complete(
            Enum.Parse<TestExecutionStatus>(snapshot.Status.ToString()),
            completedAt,
            snapshot.ErrorMessage);
        await executionRepository.AddAsync(execution, cancellationToken);
        RecentExecutions.Insert(0, execution);
    }

    private static StartTestExecutionRequest CreateRequest(Guid executionId, Guid sessionId, TestCase testCase) =>
        new(
            executionId,
            sessionId,
            testCase.Id,
            testCase.Name,
            testCase.Steps.OrderBy(step => step.Sequence).Select(step => new ExecutableTestStepDto(
                step.Id,
                step.Sequence,
                Enum.Parse<TestActionDto>(step.Action.ToString()),
                new LocatorSetDto(step.Target.Locators.Select(locator => new ElementLocatorDto(
                    Enum.Parse<LocatorKindDto>(locator.Kind.ToString()),
                    locator.Value,
                    locator.Score)).ToArray()),
                step.Input,
                step.TimeoutMilliseconds,
                step.MaxAttempts)).ToArray());
}
