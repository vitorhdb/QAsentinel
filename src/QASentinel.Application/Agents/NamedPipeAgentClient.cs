using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using QASentinel.Contracts;

namespace QASentinel.Application.Agents;

public sealed class NamedPipeAgentClient : IAgentClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public bool IsConnected => _stream?.IsConnected == true;

    public async Task<HandshakeResponse> ConnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
            {
                DisposeConnection();
                _stream = new NamedPipeClientStream(
                    ".",
                    AgentProtocol.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                await _stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                _reader = new StreamReader(_stream, Encoding.UTF8, true, 4096, leaveOpen: true);
                _writer = new StreamWriter(_stream, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true
                };
            }

            return await SendCoreAsync<HandshakeRequest, HandshakeResponse>(
                AgentOperations.Handshake,
                new HandshakeRequest("QASentinel.Desktop", typeof(NamedPipeAgentClient).Assembly.GetName().Version?.ToString() ?? "1.0.0"),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<GetProcessesResponse> GetProcessesAsync(CancellationToken cancellationToken) =>
        SendAsync<GetProcessesRequest, GetProcessesResponse>(AgentOperations.GetProcesses, new GetProcessesRequest(), cancellationToken);

    public Task<AttachApplicationResponse> AttachAsync(AttachApplicationRequest request, CancellationToken cancellationToken) =>
        SendAsync<AttachApplicationRequest, AttachApplicationResponse>(AgentOperations.Attach, request, cancellationToken);

    public Task StartInspectorAsync(StartInspectorRequest request, CancellationToken cancellationToken) =>
        SendWithoutResultAsync(AgentOperations.StartInspector, request, cancellationToken);

    public Task StopInspectorAsync(StopInspectorRequest request, CancellationToken cancellationToken) =>
        SendWithoutResultAsync(AgentOperations.StopInspector, request, cancellationToken);

    public Task<InspectorStateResponse> GetInspectorStateAsync(CancellationToken cancellationToken) =>
        SendAsync<object, InspectorStateResponse>(AgentOperations.GetCurrentElement, new { }, cancellationToken);

    public Task ClickAsync(ElementActionRequest request, CancellationToken cancellationToken) =>
        SendWithoutResultAsync(AgentOperations.Click, request, cancellationToken);

    public Task FocusAsync(ElementActionRequest request, CancellationToken cancellationToken) =>
        SendWithoutResultAsync(AgentOperations.Focus, request, cancellationToken);

    public Task InputAsync(InputElementRequest request, CancellationToken cancellationToken) =>
        SendWithoutResultAsync(AgentOperations.Input, request, cancellationToken);

    public Task<CaptureScreenshotResponse> CaptureScreenshotAsync(CaptureScreenshotRequest request, CancellationToken cancellationToken) =>
        SendAsync<CaptureScreenshotRequest, CaptureScreenshotResponse>(AgentOperations.Screenshot, request, cancellationToken);

    public Task<TestExecutionStateResponse> StartExecutionAsync(
        StartTestExecutionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<StartTestExecutionRequest, TestExecutionStateResponse>(
            AgentOperations.StartExecution,
            request,
            cancellationToken);

    public Task<TestExecutionStateResponse> GetExecutionAsync(
        GetTestExecutionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<GetTestExecutionRequest, TestExecutionStateResponse>(
            AgentOperations.GetExecution,
            request,
            cancellationToken);

    public Task<TestExecutionStateResponse> CancelExecutionAsync(
        CancelTestExecutionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<CancelTestExecutionRequest, TestExecutionStateResponse>(
            AgentOperations.CancelExecution,
            request,
            cancellationToken);

    public Task<RecorderStateResponse> StartRecordingAsync(
        StartRecordingRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<StartRecordingRequest, RecorderStateResponse>(
            AgentOperations.StartRecording,
            request,
            cancellationToken);

    public Task<RecorderStateResponse> GetRecordingAsync(CancellationToken cancellationToken) =>
        SendAsync<GetRecordingStateRequest, RecorderStateResponse>(
            AgentOperations.GetRecording,
            new GetRecordingStateRequest(),
            cancellationToken);

    public Task<RecorderStateResponse> StopRecordingAsync(
        StopRecordingRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<StopRecordingRequest, RecorderStateResponse>(
            AgentOperations.StopRecording,
            request,
            cancellationToken);

    private async Task SendWithoutResultAsync<TRequest>(string operation, TRequest request, CancellationToken cancellationToken)
    {
        _ = await SendAsync<TRequest, OperationResponse>(operation, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        string operation,
        TRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendCoreAsync<TRequest, TResponse>(operation, request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TResponse> SendCoreAsync<TRequest, TResponse>(
        string operation,
        TRequest request,
        CancellationToken cancellationToken)
    {
        if (_writer is null || _reader is null || !IsConnected)
        {
            throw new IOException("O cliente ainda não está conectado ao Agent.");
        }

        var requestId = Guid.NewGuid();
        var envelope = new IpcRequestEnvelope(AgentProtocol.Version, requestId, operation, ContractJson.ToElement(request));
        var json = JsonSerializer.Serialize(envelope, ContractJson.Options);
        if (json.Length > AgentProtocol.MaximumMessageCharacters)
        {
            throw new InvalidOperationException("A requisição excede o limite do protocolo local.");
        }

        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        var responseLine = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("O Agent encerrou a conexão sem responder.");

        if (responseLine.Length > AgentProtocol.MaximumMessageCharacters)
        {
            throw new IOException("A resposta do Agent excede o limite do protocolo local.");
        }

        var response = JsonSerializer.Deserialize<IpcResponseEnvelope>(responseLine, ContractJson.Options)
            ?? throw new IOException("O Agent retornou uma resposta inválida.");

        if (response.RequestId != requestId)
        {
            throw new IOException("A resposta do Agent não corresponde à requisição enviada.");
        }

        if (!response.Success)
        {
            var error = response.Error ?? new AgentErrorDto("unknown", "Falha não classificada no Agent.");
            throw new AgentOperationException(error.Code, error.Message, error.Detail);
        }

        if (response.Payload is not { } payload)
        {
            throw new IOException("O Agent respondeu sem o payload esperado.");
        }

        return payload.Deserialize<TResponse>(ContractJson.Options)
            ?? throw new IOException("Não foi possível interpretar a resposta do Agent.");
    }

    public ValueTask DisposeAsync()
    {
        DisposeConnection();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private void DisposeConnection()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _writer = null;
        _reader = null;
        _stream = null;
    }
}
