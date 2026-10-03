using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Executions;
using QASentinel.Automation.Abstractions;
using QASentinel.Contracts;
using QASentinel.Domain.Testing;

namespace QASentinel.Agent;

public sealed class AgentExecutionCoordinator(
    ITestRunner runner,
    IAutomationEngine automationEngine,
    ILogger<AgentExecutionCoordinator> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ExecutionEntry> _executions = new();

    public TestExecutionStateResponse Start(StartTestExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExecutionId == Guid.Empty)
        {
            throw new InvalidOperationException("O identificador da execução é obrigatório.");
        }

        var session = automationEngine.GetSession(request.SessionId);
        var definition = request.ToDefinition();
        var applicationVersion = GetApplicationVersion(session.ExecutablePath);
        var timeout = GetMaximumExecutionTime(definition);
        var entry = new ExecutionEntry(
            request.ExecutionId,
            request.TestCaseId,
            request.SessionId,
            Environment.MachineName,
            applicationVersion,
            DateTimeOffset.UtcNow,
            timeout);
        if (!_executions.TryAdd(request.ExecutionId, entry))
        {
            throw new InvalidOperationException($"A execução {request.ExecutionId} já existe no Agent.");
        }

        entry.SetTask(Task.Run(() => RunAsync(entry, definition), CancellationToken.None));
        logger.LogInformation(
            "Operation=StartExecution ExecutionId={ExecutionId} TestCaseId={TestCaseId} SessionId={SessionId} MaximumDurationMs={MaximumDurationMs} Result=Started",
            request.ExecutionId,
            request.TestCaseId,
            request.SessionId,
            timeout.TotalMilliseconds);
        return entry.Snapshot();
    }

    public TestExecutionStateResponse Get(Guid executionId) => GetEntry(executionId).Snapshot();

    public TestExecutionStateResponse Cancel(Guid executionId)
    {
        var entry = GetEntry(executionId);
        entry.Cancel();
        logger.LogInformation(
            "Operation=CancelExecution ExecutionId={ExecutionId} Result=Requested",
            executionId);
        return entry.Snapshot();
    }

    private async Task RunAsync(ExecutionEntry entry, TestCaseDefinition definition)
    {
        try
        {
            var result = await runner.RunAsync(
                entry.ExecutionId,
                entry.SessionId,
                definition,
                entry.AddStep,
                entry.CancellationToken).ConfigureAwait(false);
            entry.Complete(result);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Operation=RunExecution ExecutionId={ExecutionId} Result=Failure",
                entry.ExecutionId);
            entry.Fail("O Agent não conseguiu concluir a execução.");
        }
    }

    private ExecutionEntry GetEntry(Guid executionId) =>
        _executions.TryGetValue(executionId, out var entry)
            ? entry
            : throw new ExecutionNotFoundException(executionId);

    private static TimeSpan GetMaximumExecutionTime(TestCaseDefinition definition)
    {
        var requestedMilliseconds = definition.Steps.Sum(step => (long)step.Timeout.TotalMilliseconds) + 30_000L;
        return TimeSpan.FromMilliseconds(Math.Clamp(requestedMilliseconds, 30_000L, 1_800_000L));
    }

    private static string? GetApplicationVersion(string? executablePath)
    {
        var path = executablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _executions.Values)
        {
            entry.Cancel();
        }

        var runningTasks = _executions.Values.Select(entry => entry.Task).Where(task => task is not null).ToArray();
        if (runningTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(runningTasks!).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
            {
                logger.LogWarning("Operation=DisposeExecutionCoordinator Result=TimedOut");
            }
        }

        foreach (var entry in _executions.Values)
        {
            entry.Dispose();
        }

        _executions.Clear();
    }

    private sealed class ExecutionEntry : IDisposable
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _cancellation;
        private readonly List<StepRunResult> _steps = [];
        private TestRunResult? _result;
        private string? _fatalError;

        internal ExecutionEntry(
            Guid executionId,
            Guid testCaseId,
            Guid sessionId,
            string machineName,
            string? applicationVersion,
            DateTimeOffset startedAt,
            TimeSpan timeout)
        {
            ExecutionId = executionId;
            TestCaseId = testCaseId;
            SessionId = sessionId;
            MachineName = machineName;
            ApplicationVersion = applicationVersion;
            StartedAt = startedAt;
            _cancellation = new CancellationTokenSource(timeout);
        }

        internal Guid ExecutionId { get; }

        internal Guid TestCaseId { get; }

        internal Guid SessionId { get; }

        internal string MachineName { get; }

        internal string? ApplicationVersion { get; }

        internal DateTimeOffset StartedAt { get; }

        internal CancellationToken CancellationToken => _cancellation.Token;

        internal Task? Task { get; private set; }

        internal void SetTask(Task task) => Task = task;

        internal void AddStep(StepRunResult result)
        {
            lock (_sync)
            {
                var index = _steps.FindIndex(step => step.StepId == result.StepId);
                if (index >= 0)
                {
                    _steps[index] = result;
                }
                else
                {
                    _steps.Add(result);
                }
            }
        }

        internal void Complete(TestRunResult result)
        {
            lock (_sync)
            {
                _result = result;
                _steps.Clear();
                _steps.AddRange(result.Steps);
            }
        }

        internal void Fail(string message)
        {
            lock (_sync)
            {
                _fatalError = message;
            }
        }

        internal void Cancel() => _cancellation.Cancel();

        internal TestExecutionStateResponse Snapshot()
        {
            lock (_sync)
            {
                var completedAt = _result?.CompletedAt ?? (_fatalError is null ? null : DateTimeOffset.UtcNow);
                var status = _result?.Status ?? (_fatalError is null
                    ? TestExecutionStatus.Running
                    : TestExecutionStatus.Error);
                var duration = (long)((completedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalMilliseconds;
                return new TestExecutionStateResponse(
                    ExecutionId,
                    TestCaseId,
                    SessionId,
                    status.ToDto(),
                    _result?.StartedAt ?? StartedAt,
                    completedAt,
                    Math.Max(0, duration),
                    MachineName,
                    ApplicationVersion,
                    _steps.OrderBy(step => step.Sequence).Select(step => step.ToDto()).ToArray(),
                    _result?.ErrorMessage ?? _fatalError);
            }
        }

        public void Dispose() => _cancellation.Dispose();
    }
}

public sealed class ExecutionNotFoundException(Guid executionId)
    : Exception($"A execução '{executionId}' não existe ou expirou no Agent.");

internal static class ExecutionContractMapper
{
    internal static TestCaseDefinition ToDefinition(this StartTestExecutionRequest request) =>
        new(
            request.TestCaseId,
            request.TestCaseName,
            request.Steps.Select(step => new TestStepDefinition(
                step.StepId,
                step.Sequence,
                Enum.Parse<TestActionType>(step.Action.ToString()),
                new TestTarget(step.Target.Locators.Select(locator => new TestLocator(
                    Enum.Parse<TestLocatorKind>(locator.Kind.ToString()),
                    locator.Value,
                    locator.Score)).ToArray()),
                step.Input,
                TimeSpan.FromMilliseconds(step.TimeoutMilliseconds),
                step.MaxAttempts)).ToArray());

    internal static StepExecutionResultDto ToDto(this StepRunResult result) =>
        new(
            result.StepId,
            result.Sequence,
            Enum.Parse<TestActionDto>(result.Action.ToString()),
            Enum.Parse<StepExecutionStatusDto>(result.Status.ToString()),
            result.Attempts,
            result.StartedAt,
            result.CompletedAt,
            result.DurationMilliseconds,
            result.ErrorCode,
            result.Message);

    internal static TestExecutionStatusDto ToDto(this TestExecutionStatus status) =>
        Enum.Parse<TestExecutionStatusDto>(status.ToString());
}
