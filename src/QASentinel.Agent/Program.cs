using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QASentinel.Agent;
using QASentinel.Application;
using QASentinel.Application.Executions;
using QASentinel.Automation.UIA;
using QASentinel.Automation.Win32;
using QASentinel.Inspector;
using QASentinel.Observability;
using QASentinel.Recorder;

var builder = Host.CreateApplicationBuilder(args);
builder.AddQASentinelObservability("Agent");
builder.Services
    .AddApplication()
    .AddWin32Automation()
    .AddUia3Automation()
    .AddInspector()
    .AddRecorder();
builder.Services.AddSingleton<ITestAutomationDriver, AgentTestAutomationDriver>();
builder.Services.AddSingleton<AgentExecutionCoordinator>();
builder.Services.AddSingleton<AgentCommandHandler>();
builder.Services.AddHostedService<NamedPipeAgentServer>();

using var host = builder.Build();
var recorderProbeExitCode = await RecorderProbeCommand.TryRunAsync(args, host.Services)
    .ConfigureAwait(false);
if (recorderProbeExitCode.HasValue)
{
    Environment.ExitCode = recorderProbeExitCode.Value;
    return;
}

var runnerProbeExitCode = await RunnerProbeCommand.TryRunAsync(args, host.Services)
    .ConfigureAwait(false);
if (runnerProbeExitCode.HasValue)
{
    Environment.ExitCode = runnerProbeExitCode.Value;
    return;
}

var calibrationExitCode = await ApplicationCalibrationCommand.TryRunAsync(args, host.Services)
    .ConfigureAwait(false);
if (calibrationExitCode.HasValue)
{
    Environment.ExitCode = calibrationExitCode.Value;
    return;
}

var compatibilityProbeExitCode = await CompatibilityProbeCommand.TryRunAsync(args, host.Services)
    .ConfigureAwait(false);
if (compatibilityProbeExitCode.HasValue)
{
    Environment.ExitCode = compatibilityProbeExitCode.Value;
    return;
}

await host.RunAsync().ConfigureAwait(false);
