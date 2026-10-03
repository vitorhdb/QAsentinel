using QASentinel.Contracts;

namespace QASentinel.Application.Agents;

public interface IAgentClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<HandshakeResponse> ConnectAsync(CancellationToken cancellationToken);

    Task<GetProcessesResponse> GetProcessesAsync(CancellationToken cancellationToken);

    Task<AttachApplicationResponse> AttachAsync(AttachApplicationRequest request, CancellationToken cancellationToken);

    Task StartInspectorAsync(StartInspectorRequest request, CancellationToken cancellationToken);

    Task StopInspectorAsync(StopInspectorRequest request, CancellationToken cancellationToken);

    Task<InspectorStateResponse> GetInspectorStateAsync(CancellationToken cancellationToken);

    Task ClickAsync(ElementActionRequest request, CancellationToken cancellationToken);

    Task FocusAsync(ElementActionRequest request, CancellationToken cancellationToken);

    Task InputAsync(InputElementRequest request, CancellationToken cancellationToken);

    Task<CaptureScreenshotResponse> CaptureScreenshotAsync(CaptureScreenshotRequest request, CancellationToken cancellationToken);

    Task<TestExecutionStateResponse> StartExecutionAsync(
        StartTestExecutionRequest request,
        CancellationToken cancellationToken);

    Task<TestExecutionStateResponse> GetExecutionAsync(
        GetTestExecutionRequest request,
        CancellationToken cancellationToken);

    Task<TestExecutionStateResponse> CancelExecutionAsync(
        CancelTestExecutionRequest request,
        CancellationToken cancellationToken);

    Task<RecorderStateResponse> StartRecordingAsync(
        StartRecordingRequest request,
        CancellationToken cancellationToken);

    Task<RecorderStateResponse> GetRecordingAsync(CancellationToken cancellationToken);

    Task<RecorderStateResponse> StopRecordingAsync(
        StopRecordingRequest request,
        CancellationToken cancellationToken);
}

public sealed class AgentOperationException(string code, string message, string? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public string? Detail { get; } = detail;
}
