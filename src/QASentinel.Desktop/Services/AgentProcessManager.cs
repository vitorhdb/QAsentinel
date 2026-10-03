using System.Diagnostics;
using System.IO;

namespace QASentinel.Desktop.Services;

public sealed class AgentProcessManager : IAsyncDisposable
{
    private Process? _ownedProcess;

    public Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Process.GetProcessesByName("QASentinel.Agent").Length > 0)
        {
            return Task.CompletedTask;
        }

        var executablePath = ResolveAgentPath();
        var currentHost = Environment.ProcessPath;
        var runningUnderDotnet = string.Equals(
            Path.GetFileNameWithoutExtension(currentHost),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
        var fileName = runningUnderDotnet && !string.IsNullOrWhiteSpace(currentHost)
            ? currentHost
            : executablePath;
        var arguments = runningUnderDotnet
            ? $"\"{Path.ChangeExtension(executablePath, ".dll")}\""
            : string.Empty;
        _ownedProcess = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
        }) ?? throw new InvalidOperationException("Não foi possível iniciar o processo QASentinel.Agent.");
        return Task.CompletedTask;
    }

    private static string ResolveAgentPath()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "Agent", "QASentinel.Agent.exe");
        if (File.Exists(packaged))
        {
            return packaged;
        }

        throw new FileNotFoundException("O executável QASentinel.Agent.exe não foi encontrado.", packaged);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownedProcess is { HasExited: false })
        {
            _ownedProcess.Kill(entireProcessTree: true);
            _ownedProcess.WaitForExit(2000);
        }

        _ownedProcess?.Dispose();
        return ValueTask.CompletedTask;
    }
}
