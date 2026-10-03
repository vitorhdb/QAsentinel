using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Agents;
using QASentinel.Contracts;
using QASentinel.Desktop.State;

namespace QASentinel.Desktop.ViewModels;

public sealed partial class InspectorViewModel(
    IAgentClient agentClient,
    AppState state,
    TestDraftState testDraft,
    ILogger<InspectorViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;

    public AppState State { get; } = state;

    public ObservableCollection<ProcessInfoDto> Processes { get; } = new();

    [ObservableProperty]
    private ProcessInfoDto? _selectedProcess;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isInspectorRunning;

    [ObservableProperty]
    private string _statusMessage = "Atualize a lista e selecione uma aplicação com janela visível.";

    [ObservableProperty]
    private string _inputText = "Texto enviado pelo QA Sentinel";

    [ObservableProperty]
    private string _processFilter = string.Empty;

    public IReadOnlyList<TestActionOption> DraftActions { get; } =
    [
        new(TestActionDto.Click, "Clicar"),
        new(TestActionDto.Focus, "Focar"),
        new(TestActionDto.Input, "Digitar texto"),
        new(TestActionDto.WaitForElement, "Aguardar elemento"),
        new(TestActionDto.AssertExists, "Validar existência"),
        new(TestActionDto.AssertVisible, "Validar visibilidade"),
        new(TestActionDto.AssertEnabled, "Validar habilitado")
    ];

    [ObservableProperty]
    private TestActionOption _selectedDraftAction = new(TestActionDto.Click, "Clicar");

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            var response = await agentClient.GetProcessesAsync(cancellationToken);
            var filter = ProcessFilter.Trim();
            var processes = string.IsNullOrWhiteSpace(filter)
                ? response.Processes
                : response.Processes.Where(process =>
                    process.ProcessName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                    process.WindowTitle.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();

            Processes.Clear();
            foreach (var process in processes)
            {
                Processes.Add(process);
            }

            StatusMessage = $"{Processes.Count} processo(s) com janela encontrado(s).";
        });
    }

    [RelayCommand]
    private async Task AttachSelectedAsync()
    {
        if (SelectedProcess is null)
        {
            StatusMessage = "Selecione um processo antes de conectar.";
            return;
        }

        await StopInspectorAsync();
        await ExecuteAsync(async cancellationToken =>
        {
            var response = await agentClient.AttachAsync(
                new AttachApplicationRequest(SelectedProcess.ProcessId),
                cancellationToken);
            State.Session = response.Session;
            State.CurrentElement = null;
            State.ConnectionStatus = $"Conectado · {response.Session.ProcessName} · {response.Session.AutomationBackend}";
            StatusMessage = $"Conectado à janela “{response.Session.WindowTitle}”. Ative o Inspector.";
        });
    }

    [RelayCommand]
    private async Task ToggleInspectorAsync()
    {
        if (IsInspectorRunning)
        {
            await StopInspectorAsync();
            return;
        }

        if (State.Session is null)
        {
            StatusMessage = "Conecte a uma aplicação antes de ativar o Inspector.";
            return;
        }

        await ExecuteAsync(async cancellationToken =>
        {
            await agentClient.StartInspectorAsync(new StartInspectorRequest(State.Session.SessionId), cancellationToken);
            IsInspectorRunning = true;
            StatusMessage = "Inspector ativo. Mova o cursor sobre a aplicação conectada.";
            _pollCancellation = new CancellationTokenSource();
            _pollTask = PollInspectorAsync(_pollCancellation.Token);
        });
    }

    public async Task StopInspectorAsync()
    {
        var session = State.Session;
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

        if (IsInspectorRunning && session is not null && agentClient.IsConnected)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await agentClient.StopInspectorAsync(new StopInspectorRequest(session.SessionId), timeout.Token);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Operation=StopInspector Result=Failure");
            }
        }

        IsInspectorRunning = false;
        State.CurrentElement = null;
        StatusMessage = "Inspector inativo.";
    }

    [RelayCommand]
    private Task TestClickAsync() => ExecuteOnCurrentElementAsync((sessionId, target, cancellationToken) =>
        agentClient.ClickAsync(new ElementActionRequest(sessionId, target), cancellationToken), "Clique executado.");

    [RelayCommand]
    private Task FocusAsync() => ExecuteOnCurrentElementAsync((sessionId, target, cancellationToken) =>
        agentClient.FocusAsync(new ElementActionRequest(sessionId, target), cancellationToken), "Foco aplicado.");

    [RelayCommand]
    private Task SendTextAsync() => ExecuteOnCurrentElementAsync((sessionId, target, cancellationToken) =>
        agentClient.InputAsync(new InputElementRequest(sessionId, target, InputText), cancellationToken), "Texto enviado.");

    [RelayCommand]
    private Task ScreenshotElementAsync() => CaptureScreenshotAsync(ScreenshotTargetDto.Element);

    [RelayCommand]
    private Task ScreenshotApplicationAsync() => CaptureScreenshotAsync(ScreenshotTargetDto.Application);

    [RelayCommand]
    private void AddToTest()
    {
        if (State.CurrentElement is null)
        {
            StatusMessage = "Selecione um componente antes de adicioná-lo ao teste.";
            return;
        }

        testDraft.Add(SelectedDraftAction.Action, State.CurrentElement, InputText);
        StatusMessage = $"Passo {testDraft.Steps.Count} adicionado ao rascunho de teste.";
    }

    private async Task CaptureScreenshotAsync(ScreenshotTargetDto target)
    {
        if (State.Session is null || (target == ScreenshotTargetDto.Element && State.CurrentElement is null))
        {
            StatusMessage = "Conecte à aplicação e selecione um componente antes da captura.";
            return;
        }

        await ExecuteAsync(async cancellationToken =>
        {
            var response = await agentClient.CaptureScreenshotAsync(
                new CaptureScreenshotRequest(
                    State.Session.SessionId,
                    target,
                    target == ScreenshotTargetDto.Element ? State.CurrentElement?.Locators : null),
                cancellationToken);
            State.LastScreenshotPath = response.FilePath;
            StatusMessage = $"Screenshot salvo em {response.FilePath}";
        });
    }

    private async Task ExecuteOnCurrentElementAsync(
        Func<Guid, LocatorSetDto, CancellationToken, Task> operation,
        string successMessage)
    {
        if (State.Session is null || State.CurrentElement is null)
        {
            StatusMessage = "Nenhum componente está selecionado pelo Inspector.";
            return;
        }

        await ExecuteAsync(async cancellationToken =>
        {
            await operation(State.Session.SessionId, State.CurrentElement.Locators, cancellationToken);
            StatusMessage = successMessage;
        });
    }

    private async Task PollInspectorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(160));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var snapshot = await agentClient.GetInspectorStateAsync(cancellationToken);
                State.CurrentElement = snapshot.Element;
                if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
                {
                    StatusMessage = snapshot.StatusMessage;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Operation=PollInspector Result=Failure");
                StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
                break;
            }
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await action(timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=InspectorUiCommand Result=Failure");
            StatusMessage = AgentErrorMessageMapper.ToFriendlyMessage(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ShutdownAsync() => StopInspectorAsync();
}

public sealed record TestActionOption(TestActionDto Action, string Label);
