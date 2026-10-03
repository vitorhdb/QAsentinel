using System.Diagnostics;
using QASentinel.Application.Agents;
using QASentinel.Contracts;

namespace QASentinel.IntegrationTests;

public sealed class NamedPipeHandshakeTests
{
    [Fact]
    public async Task Client_HandshakesAndListsProcessesThroughRealAgent()
    {
        var agentDll = ResolveAgentPath();
        var dotnetHost = ResolveDotnetHost();
        using var agent = Process.Start(new ProcessStartInfo
        {
            FileName = dotnetHost,
            Arguments = $"\"{agentDll}\"",
            WorkingDirectory = Path.GetDirectoryName(agentDll)!,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Não foi possível iniciar o Agent de integração.");
        await using var client = new NamedPipeAgentClient();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var handshake = await client.ConnectAsync(timeout.Token);
            var processes = await client.GetProcessesAsync(timeout.Token);

            Assert.Equal(AgentProtocol.Version, handshake.ProtocolVersion);
            Assert.Equal("UIA3", handshake.AutomationBackend);
            Assert.NotNull(processes.Processes);
        }
        finally
        {
            if (!agent.HasExited)
            {
                agent.Kill(entireProcessTree: true);
                await agent.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public async Task Client_RunsTestAsynchronouslyThroughRealAgent()
    {
        var playgroundPath = ResolvePlaygroundPath();
        using var playground = Process.Start(new ProcessStartInfo(playgroundPath) { UseShellExecute = true })
            ?? throw new InvalidOperationException("Não foi possível iniciar o Test Playground.");
        var agentDll = ResolveAgentPath();
        using var agent = Process.Start(new ProcessStartInfo
        {
            FileName = ResolveDotnetHost(),
            Arguments = $"\"{agentDll}\"",
            WorkingDirectory = Path.GetDirectoryName(agentDll)!,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Não foi possível iniciar o Agent de integração.");
        await using var client = new NamedPipeAgentClient();

        try
        {
            playground.WaitForInputIdle(10_000);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _ = await client.ConnectAsync(timeout.Token);
            var attached = await client.AttachAsync(new AttachApplicationRequest(playground.Id, 10_000), timeout.Token);
            var target = new LocatorSetDto(
            [
                new ElementLocatorDto(LocatorKindDto.AutomationId, "txtName", 100),
                new ElementLocatorDto(LocatorKindDto.Name, "Nome", 85)
            ]);
            var executionId = Guid.NewGuid();
            var started = await client.StartExecutionAsync(
                new StartTestExecutionRequest(
                    executionId,
                    attached.Session.SessionId,
                    Guid.NewGuid(),
                    "Preencher nome no Playground",
                    [
                        new ExecutableTestStepDto(
                            Guid.NewGuid(),
                            1,
                            TestActionDto.Input,
                            target,
                            "M2 Runner",
                            5_000,
                            2),
                        new ExecutableTestStepDto(
                            Guid.NewGuid(),
                            2,
                            TestActionDto.AssertVisible,
                            target,
                            null,
                            2_000,
                            1)
                    ]),
                timeout.Token);

            Assert.Equal(TestExecutionStatusDto.Running, started.Status);
            TestExecutionStateResponse current;
            do
            {
                await Task.Delay(100, timeout.Token);
                current = await client.GetExecutionAsync(
                    new GetTestExecutionRequest(executionId),
                    timeout.Token);
            }
            while (current.Status == TestExecutionStatusDto.Running);

            Assert.Equal(TestExecutionStatusDto.Passed, current.Status);
            Assert.Equal(2, current.Steps.Count);
            Assert.All(current.Steps, step => Assert.Equal(StepExecutionStatusDto.Passed, step.Status));
        }
        finally
        {
            if (!agent.HasExited)
            {
                agent.Kill(entireProcessTree: true);
                await agent.WaitForExitAsync();
            }

            if (!playground.HasExited)
            {
                playground.Kill(entireProcessTree: true);
                await playground.WaitForExitAsync();
            }
        }
    }

    private static string ResolveAgentPath()
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var path = Path.Combine(
            workspace,
            "src",
            "QASentinel.Agent",
            "bin",
            configuration,
            "net10.0-windows",
            "QASentinel.Agent.dll");
        Assert.True(File.Exists(path), $"Agent não encontrado em {path}");
        return path;
    }

    private static string ResolvePlaygroundPath()
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var executable = Path.Combine(
            workspace,
            "src",
            "QASentinel.TestPlayground",
            "bin",
            configuration,
            "net10.0-windows",
            "QASentinel.TestPlayground.exe");
        Assert.True(File.Exists(executable), $"Playground não encontrado em {executable}");
        return executable;
    }

    private static string ResolveDotnetHost()
    {
        var configuredHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configuredHost) && File.Exists(configuredHost))
        {
            return configuredHost;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return "dotnet";
    }
}
