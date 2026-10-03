using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QASentinel.Contracts;

namespace QASentinel.Agent;

public sealed class NamedPipeAgentServer(
    AgentCommandHandler commandHandler,
    ILogger<NamedPipeAgentServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Operation=AgentStarted PipeName={PipeName} ProtocolVersion={ProtocolVersion}",
            AgentProtocol.PipeName,
            AgentProtocol.Version);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                AgentProtocol.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Operation=AgentClientConnected Result=Success");
                await HandleConnectionAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Operation=AgentConnection Result=Disconnected");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Operation=AgentConnection Result=Failure");
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, true, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        var handshakeCompleted = false;

        while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            logger.LogDebug("Operation=AgentRequestReceived MessageLength={MessageLength}", line.Length);

            IpcRequestEnvelope? request = null;
            IpcResponseEnvelope response;
            try
            {
                if (line.Length > AgentProtocol.MaximumMessageCharacters)
                {
                    throw new InvalidOperationException("Mensagem IPC maior que o limite permitido.");
                }

                request = JsonSerializer.Deserialize<IpcRequestEnvelope>(line, ContractJson.Options)
                    ?? throw new JsonException("Envelope IPC ausente.");
                if (request.ProtocolVersion != AgentProtocol.Version)
                {
                    throw new InvalidOperationException(
                        $"Versão de protocolo incompatível. Agent={AgentProtocol.Version}, Cliente={request.ProtocolVersion}.");
                }

                if (!handshakeCompleted && request.Operation != AgentOperations.Handshake)
                {
                    throw new InvalidOperationException("O handshake deve ser a primeira operação da conexão.");
                }

                using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                operationTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                var payload = await commandHandler.HandleAsync(request, operationTimeout.Token).ConfigureAwait(false);
                handshakeCompleted |= request.Operation == AgentOperations.Handshake;
                response = new IpcResponseEnvelope(
                    AgentProtocol.Version, request.RequestId, true, ContractJson.ToElement(payload), null);
            }
            catch (Exception exception)
            {
                var error = AgentErrorMapper.Map(exception);
                logger.LogError(
                    exception,
                    "Operation={Operation} RequestId={RequestId} ErrorCode={ErrorCode} Result=Failure",
                    request?.Operation ?? "DeserializeRequest", request?.RequestId, error.Code);
                response = new IpcResponseEnvelope(
                    AgentProtocol.Version, request?.RequestId ?? Guid.Empty, false, null, error);
            }

            var json = JsonSerializer.Serialize(response, ContractJson.Options);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            logger.LogDebug(
                "Operation=AgentResponseSent RequestId={RequestId} Success={Success} MessageLength={MessageLength}",
                response.RequestId,
                response.Success,
                json.Length);
        }

        logger.LogInformation("Operation=AgentClientDisconnected Result=Success");
    }
}
