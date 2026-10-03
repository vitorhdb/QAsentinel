using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QASentinel.Automation.Abstractions;
using QASentinel.Contracts;
using QASentinel.Inspector;
using QASentinel.Recorder;

namespace QASentinel.Agent;

public sealed class AgentCommandHandler(
    IProcessService processService,
    IAutomationEngine automationEngine,
    IHoverInspector inspector,
    IScreenshotService screenshotService,
    AgentExecutionCoordinator executionCoordinator,
    IDesktopInputRecorder recorder,
    ILogger<AgentCommandHandler> logger)
{
    public async Task<object> HandleAsync(IpcRequestEnvelope request, CancellationToken cancellationToken) =>
        request.Operation switch
        {
            AgentOperations.Handshake => HandleHandshake(request.Payload),
            AgentOperations.GetProcesses => await GetProcessesAsync(cancellationToken).ConfigureAwait(false),
            AgentOperations.Attach => await AttachAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.StartInspector => await StartInspectorAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.StopInspector => await StopInspectorAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.GetCurrentElement => GetInspectorState(),
            AgentOperations.Click => await ClickAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.Focus => await FocusAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.Input => await InputAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.Screenshot => await CaptureScreenshotAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.StartExecution => StartExecution(request.Payload),
            AgentOperations.GetExecution => GetExecution(request.Payload),
            AgentOperations.CancelExecution => CancelExecution(request.Payload),
            AgentOperations.StartRecording => await StartRecordingAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            AgentOperations.GetRecording => GetRecordingState(),
            AgentOperations.StopRecording => await StopRecordingAsync(request.Payload, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Operação IPC desconhecida: {request.Operation}")
        };

    private static HandshakeResponse HandleHandshake(JsonElement payload)
    {
        _ = Deserialize<HandshakeRequest>(payload);
        return new HandshakeResponse(
            typeof(AgentCommandHandler).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            AgentProtocol.Version,
            Environment.MachineName,
            "UIA3");
    }

    private async Task<GetProcessesResponse> GetProcessesAsync(CancellationToken cancellationToken)
    {
        var processes = await processService.GetProcessesAsync(cancellationToken).ConfigureAwait(false);
        return new GetProcessesResponse(processes.Select(process => process.ToDto()).ToArray());
    }

    private async Task<AttachApplicationResponse> AttachAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<AttachApplicationRequest>(payload);
        await inspector.StopAsync(cancellationToken).ConfigureAwait(false);
        await StopRecorderIfRunningAsync(cancellationToken).ConfigureAwait(false);
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(request.TimeoutMilliseconds, 500, 30_000));
        var session = await automationEngine.AttachAsync(request.ProcessId, timeout, cancellationToken).ConfigureAwait(false);
        return new AttachApplicationResponse(session.ToDto());
    }

    private async Task<OperationResponse> StartInspectorAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<StartInspectorRequest>(payload);
        await StopRecorderIfRunningAsync(cancellationToken).ConfigureAwait(false);
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(request.PollIntervalMilliseconds, 80, 1000));
        await inspector.StartAsync(request.SessionId, interval, cancellationToken).ConfigureAwait(false);
        return new OperationResponse(true, "Inspector iniciado.");
    }

    private async Task<OperationResponse> StopInspectorAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        _ = Deserialize<StopInspectorRequest>(payload);
        await inspector.StopAsync(cancellationToken).ConfigureAwait(false);
        return new OperationResponse(true, "Inspector interrompido.");
    }

    private InspectorStateResponse GetInspectorState()
    {
        var snapshot = inspector.Current;
        return new InspectorStateResponse(snapshot.IsRunning, snapshot.Element?.ToDto(), snapshot.StatusMessage);
    }

    private async Task<OperationResponse> ClickAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<ElementActionRequest>(payload);
        await automationEngine.ClickAsync(request.SessionId, request.Target.ToModel(), cancellationToken).ConfigureAwait(false);
        return new OperationResponse(true, "Clique executado.");
    }

    private async Task<OperationResponse> FocusAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<ElementActionRequest>(payload);
        await automationEngine.FocusAsync(request.SessionId, request.Target.ToModel(), cancellationToken).ConfigureAwait(false);
        return new OperationResponse(true, "Foco aplicado.");
    }

    private async Task<OperationResponse> InputAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<InputElementRequest>(payload);
        await automationEngine.TypeTextAsync(request.SessionId, request.Target.ToModel(), request.Text, cancellationToken)
            .ConfigureAwait(false);
        return new OperationResponse(true, "Texto enviado.");
    }

    private async Task<CaptureScreenshotResponse> CaptureScreenshotAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var request = Deserialize<CaptureScreenshotRequest>(payload);
        var session = automationEngine.GetSession(request.SessionId);
        var element = request.Element is null
            ? null
            : await automationEngine.FindElementAsync(request.SessionId, request.Element.ToModel(), cancellationToken)
                .ConfigureAwait(false);
        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "QA Sentinel", "Screenshots")
            : request.OutputDirectory;
        var result = await screenshotService.CaptureAsync(
            request.Target.ToModel(), session, element, outputDirectory, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Operation=CaptureScreenshot ProcessId={ProcessId} Target={Target} FilePath={FilePath} Result=Success",
            session.ProcessId, request.Target, result.FilePath);
        return new CaptureScreenshotResponse(result.FilePath, result.Width, result.Height, result.CapturedAt);
    }

    private TestExecutionStateResponse StartExecution(JsonElement payload)
    {
        var request = Deserialize<StartTestExecutionRequest>(payload);
        return executionCoordinator.Start(request);
    }

    private TestExecutionStateResponse GetExecution(JsonElement payload)
    {
        var request = Deserialize<GetTestExecutionRequest>(payload);
        return executionCoordinator.Get(request.ExecutionId);
    }

    private TestExecutionStateResponse CancelExecution(JsonElement payload)
    {
        var request = Deserialize<CancelTestExecutionRequest>(payload);
        return executionCoordinator.Cancel(request.ExecutionId);
    }

    private async Task<RecorderStateResponse> StartRecordingAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = Deserialize<StartRecordingRequest>(payload);
        await inspector.StopAsync(cancellationToken).ConfigureAwait(false);
        await recorder.StartAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        return recorder.Current.ToDto();
    }

    private RecorderStateResponse GetRecordingState() => recorder.Current.ToDto();

    private async Task<RecorderStateResponse> StopRecordingAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = Deserialize<StopRecordingRequest>(payload);
        if (recorder.Current.IsRecording &&
            recorder.Current.SessionId is { } sessionId &&
            sessionId != request.SessionId)
        {
            throw new InvalidOperationException("A gravação ativa pertence a outra sessão.");
        }

        await recorder.StopAsync(cancellationToken).ConfigureAwait(false);
        return recorder.Current.ToDto();
    }

    private Task StopRecorderIfRunningAsync(CancellationToken cancellationToken) =>
        recorder.Current.IsRecording
            ? recorder.StopAsync(cancellationToken)
            : Task.CompletedTask;

    private static T Deserialize<T>(JsonElement payload) =>
        payload.Deserialize<T>(ContractJson.Options)
        ?? throw new JsonException($"Payload inválido para {typeof(T).Name}.");
}
