using System.IO;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Agents;
using QASentinel.Desktop.State;

namespace QASentinel.Desktop.Services;

public sealed class AgentConnectionService(
    IAgentClient client,
    AgentProcessManager processManager,
    AppState state,
    ILogger<AgentConnectionService> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await processManager.EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(TimeSpan.FromMilliseconds(500));
                var handshake = await client.ConnectAsync(attemptTimeout.Token).ConfigureAwait(false);
                state.IsAgentConnected = true;
                state.ConnectionStatus = $"Agent conectado · UIA3 · protocolo {handshake.ProtocolVersion}";
                logger.LogInformation(
                    "Operation=AgentHandshake ProtocolVersion={ProtocolVersion} AgentVersion={AgentVersion} Result=Success",
                    handshake.ProtocolVersion,
                    handshake.AgentVersion);
                return;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
            {
                lastError = exception;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        state.ConnectionStatus = "Agent indisponível";
        throw new IOException("Não foi possível estabelecer a conexão local com o Agent.", lastError);
    }
}
