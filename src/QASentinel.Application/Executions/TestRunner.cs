using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QASentinel.Domain.Testing;

namespace QASentinel.Application.Executions;

public sealed class TestRunner(
    ITestAutomationDriver driver,
    IExecutionDelay delay,
    ILogger<TestRunner> logger) : ITestRunner
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(120);

    public async Task<TestRunResult> RunAsync(
        Guid executionId,
        Guid sessionId,
        TestCaseDefinition testCase,
        Action<StepRunResult>? onStepCompleted,
        CancellationToken cancellationToken)
    {
        Validate(executionId, sessionId, testCase);
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<StepRunResult>(testCase.Steps.Count);
        var orderedSteps = testCase.Steps.OrderBy(step => step.Sequence).ToArray();

        logger.LogInformation(
            "Operation=RunTest ExecutionId={ExecutionId} TestCaseId={TestCaseId} SessionId={SessionId} StepCount={StepCount} Result=Started",
            executionId,
            testCase.TestCaseId,
            sessionId,
            orderedSteps.Length);

        TestExecutionStatus status;
        string? errorMessage = null;
        try
        {
            foreach (var step in orderedSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ExecuteStepAsync(sessionId, step, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                onStepCompleted?.Invoke(result);

                if (result.Status is StepExecutionStatus.Failed or StepExecutionStatus.Error)
                {
                    AppendSkippedSteps(orderedSteps, step.Sequence, results, onStepCompleted);
                    status = result.Status == StepExecutionStatus.Failed
                        ? TestExecutionStatus.Failed
                        : TestExecutionStatus.Error;
                    errorMessage = result.Message;
                    return Complete(executionId, sessionId, testCase, status, startedAt, results, errorMessage);
                }
            }

            status = TestExecutionStatus.Passed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = TestExecutionStatus.Cancelled;
            errorMessage = "Execução cancelada.";
            var lastSequence = results.Count == 0 ? 0 : results.Max(result => result.Sequence);
            AppendSkippedSteps(orderedSteps, lastSequence, results, onStepCompleted);
        }

        return Complete(executionId, sessionId, testCase, status, startedAt, results, errorMessage);
    }

    private async Task<StepRunResult> ExecuteStepAsync(
        Guid sessionId,
        TestStepDefinition step,
        CancellationToken executionCancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempts = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(executionCancellationToken);
        timeout.CancelAfter(step.Timeout);

        try
        {
            switch (step.Action)
            {
                case TestActionType.AssertExists:
                    attempts = await WaitForAsync(
                        sessionId,
                        step.Target,
                        state => state?.Exists == true,
                        timeout.Token).ConfigureAwait(false);
                    break;
                case TestActionType.AssertVisible:
                    attempts = await WaitForAsync(
                        sessionId,
                        step.Target,
                        state => state is { Exists: true, IsVisible: true },
                        timeout.Token).ConfigureAwait(false);
                    break;
                case TestActionType.AssertEnabled:
                    attempts = await WaitForAsync(
                        sessionId,
                        step.Target,
                        state => state is { Exists: true, IsEnabled: true },
                        timeout.Token).ConfigureAwait(false);
                    break;
                case TestActionType.WaitForElement:
                    attempts = await WaitForAsync(
                        sessionId,
                        step.Target,
                        state => state?.Exists == true,
                        timeout.Token).ConfigureAwait(false);
                    break;
                case TestActionType.Click:
                case TestActionType.Focus:
                case TestActionType.Input:
                    _ = await WaitForAsync(
                        sessionId,
                        step.Target,
                        state => state is { Exists: true, IsEnabled: true },
                        timeout.Token).ConfigureAwait(false);
                    attempts = await ExecuteActionWithRetryAsync(sessionId, step, timeout.Token).ConfigureAwait(false);
                    break;
                default:
                    throw new AutomationDriverException(
                        "unsupported_action",
                        $"A ação {step.Action} ainda não é suportada pelo Runner.",
                        false);
            }

            return CreateResult(step, StepExecutionStatus.Passed, attempts, startedAt);
        }
        catch (OperationCanceledException) when (!executionCancellationToken.IsCancellationRequested)
        {
            var assertion = IsAssertion(step.Action);
            return CreateResult(
                step,
                assertion ? StepExecutionStatus.Failed : StepExecutionStatus.Error,
                Math.Max(1, attempts),
                startedAt,
                assertion ? "assertion_timeout" : "automation_timeout",
                assertion
                    ? $"A condição {step.Action} não foi atendida em {step.Timeout.TotalSeconds:0.#} s."
                    : $"O passo excedeu o timeout de {step.Timeout.TotalSeconds:0.#} s.");
        }
        catch (AutomationDriverException exception)
        {
            logger.LogWarning(
                exception,
                "Operation=ExecuteTestStep StepId={StepId} Sequence={Sequence} Action={Action} Attempts={Attempts} Result=Error",
                step.StepId,
                step.Sequence,
                step.Action,
                attempts);
            return CreateResult(
                step,
                StepExecutionStatus.Error,
                Math.Max(1, attempts),
                startedAt,
                exception.Code,
                exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Operation=ExecuteTestStep StepId={StepId} Sequence={Sequence} Action={Action} Result=Error",
                step.StepId,
                step.Sequence,
                step.Action);
            return CreateResult(
                step,
                StepExecutionStatus.Error,
                Math.Max(1, attempts),
                startedAt,
                "runner_error",
                "O Runner encontrou uma falha inesperada.");
        }
    }

    private async Task<int> WaitForAsync(
        Guid sessionId,
        TestTarget target,
        Func<AutomationElementState?, bool> condition,
        CancellationToken cancellationToken)
    {
        var polls = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            polls++;
            AutomationElementState? state = null;
            try
            {
                state = await driver.FindAsync(sessionId, target, cancellationToken).ConfigureAwait(false);
            }
            catch (AutomationDriverException exception) when (exception.IsTransient)
            {
                logger.LogDebug(
                    exception,
                    "Operation=SmartWait Poll={Poll} ErrorCode={ErrorCode} Result=Retry",
                    polls,
                    exception.Code);
            }

            if (condition(state))
            {
                return polls;
            }

            await delay.DelayAsync(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> ExecuteActionWithRetryAsync(
        Guid sessionId,
        TestStepDefinition step,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= step.MaxAttempts; attempt++)
        {
            try
            {
                await ExecuteActionAsync(sessionId, step, cancellationToken).ConfigureAwait(false);
                return attempt;
            }
            catch (AutomationDriverException exception) when (exception.IsTransient && attempt < step.MaxAttempts)
            {
                var retryDelay = GetRetryDelay(attempt);
                logger.LogWarning(
                    exception,
                    "Operation=ExecuteTestStep StepId={StepId} Sequence={Sequence} Action={Action} Attempt={Attempt} RetryDelayMs={RetryDelayMs} Result=Retry",
                    step.StepId,
                    step.Sequence,
                    step.Action,
                    attempt,
                    retryDelay.TotalMilliseconds);
                await delay.DelayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new AutomationDriverException("retry_exhausted", "As tentativas do passo foram esgotadas.");
    }

    private Task ExecuteActionAsync(Guid sessionId, TestStepDefinition step, CancellationToken cancellationToken) =>
        step.Action switch
        {
            TestActionType.Click => driver.ClickAsync(sessionId, step.Target, cancellationToken),
            TestActionType.Focus => driver.FocusAsync(sessionId, step.Target, cancellationToken),
            TestActionType.Input => driver.InputAsync(sessionId, step.Target, step.Input ?? string.Empty, cancellationToken),
            _ => throw new AutomationDriverException(
                "unsupported_action",
                $"A ação {step.Action} não pode ser executada como interação.",
                false)
        };

    private TestRunResult Complete(
        Guid executionId,
        Guid sessionId,
        TestCaseDefinition testCase,
        TestExecutionStatus status,
        DateTimeOffset startedAt,
        IReadOnlyList<StepRunResult> results,
        string? errorMessage)
    {
        var completedAt = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Operation=RunTest ExecutionId={ExecutionId} TestCaseId={TestCaseId} Status={Status} DurationMs={DurationMs} Result=Completed",
            executionId,
            testCase.TestCaseId,
            status,
            (long)(completedAt - startedAt).TotalMilliseconds);
        return new TestRunResult(
            executionId,
            testCase.TestCaseId,
            sessionId,
            status,
            startedAt,
            completedAt,
            results.ToArray(),
            errorMessage);
    }

    private static StepRunResult CreateResult(
        TestStepDefinition step,
        StepExecutionStatus status,
        int attempts,
        DateTimeOffset startedAt,
        string? errorCode = null,
        string? message = null) =>
        new(
            step.StepId,
            step.Sequence,
            step.Action,
            status,
            attempts,
            startedAt,
            DateTimeOffset.UtcNow,
            errorCode,
            message);

    private static void AppendSkippedSteps(
        IReadOnlyList<TestStepDefinition> orderedSteps,
        int completedSequence,
        ICollection<StepRunResult> results,
        Action<StepRunResult>? onStepCompleted)
    {
        foreach (var remaining in orderedSteps.Where(step => step.Sequence > completedSequence))
        {
            var now = DateTimeOffset.UtcNow;
            var skipped = new StepRunResult(
                remaining.StepId,
                remaining.Sequence,
                remaining.Action,
                StepExecutionStatus.Skipped,
                0,
                now,
                now,
                null,
                "Passo não executado após uma falha anterior.");
            results.Add(skipped);
            onStepCompleted?.Invoke(skipped);
        }
    }

    private static TimeSpan GetRetryDelay(int failedAttempt) => failedAttempt switch
    {
        1 => TimeSpan.FromMilliseconds(200),
        2 => TimeSpan.FromMilliseconds(500),
        3 => TimeSpan.FromSeconds(1),
        _ => TimeSpan.FromSeconds(2)
    };

    private static bool IsAssertion(TestActionType action) =>
        action is TestActionType.AssertExists or TestActionType.AssertVisible or TestActionType.AssertEnabled;

    private static void Validate(Guid executionId, Guid sessionId, TestCaseDefinition testCase)
    {
        if (executionId == Guid.Empty || sessionId == Guid.Empty || testCase.TestCaseId == Guid.Empty)
        {
            throw new ArgumentException("Execução, sessão e caso de teste precisam de identificadores válidos.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(testCase.Name);
        if (testCase.Steps.Count == 0)
        {
            throw new ArgumentException("O caso de teste deve possuir ao menos um passo.", nameof(testCase));
        }

        if (testCase.Steps.Any(step => step.StepId == Guid.Empty || step.Sequence < 1 ||
                                       step.Timeout < TimeSpan.FromMilliseconds(100) ||
                                       step.Timeout > TimeSpan.FromMinutes(2) ||
                                       step.MaxAttempts is < 1 or > 10))
        {
            throw new ArgumentException("O caso de teste contém um passo inválido.", nameof(testCase));
        }

        var sequences = testCase.Steps.Select(step => step.Sequence).Order().ToArray();
        if (!sequences.SequenceEqual(Enumerable.Range(1, sequences.Length)))
        {
            throw new ArgumentException("A sequência dos passos deve ser contínua e iniciar em 1.", nameof(testCase));
        }
    }
}
