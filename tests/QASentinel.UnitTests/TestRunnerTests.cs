using Microsoft.Extensions.Logging.Abstractions;
using QASentinel.Application.Executions;
using QASentinel.Domain.Testing;

namespace QASentinel.UnitTests;

public sealed class TestRunnerTests
{
    [Fact]
    public async Task Runner_RetriesTransientInteractionAndPasses()
    {
        var driver = new FakeDriver
        {
            State = new AutomationElementState(true, true, true, false, "Salvar"),
            ClickFailuresRemaining = 2
        };
        var runner = CreateRunner(driver);
        var testCase = CreateTestCase(TestActionType.Click, maxAttempts: 3);

        var result = await runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), testCase, null, CancellationToken.None);

        Assert.Equal(TestExecutionStatus.Passed, result.Status);
        Assert.Equal(3, result.Steps[0].Attempts);
        Assert.Equal(3, driver.ClickCalls);
    }

    [Fact]
    public async Task Runner_ReportsAssertionTimeoutAsProductFailure()
    {
        var driver = new FakeDriver
        {
            State = new AutomationElementState(true, true, false, false, "Salvar")
        };
        var runner = CreateRunner(driver);
        var testCase = CreateTestCase(TestActionType.AssertVisible, timeout: TimeSpan.FromMilliseconds(120));

        var result = await runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), testCase, null, CancellationToken.None);

        Assert.Equal(TestExecutionStatus.Failed, result.Status);
        Assert.Equal(StepExecutionStatus.Failed, result.Steps[0].Status);
        Assert.Equal("assertion_timeout", result.Steps[0].ErrorCode);
    }

    [Fact]
    public async Task Runner_ReportsMissingInteractionTargetAsAutomationError()
    {
        var driver = new FakeDriver { State = null };
        var runner = CreateRunner(driver);
        var testCase = CreateTestCase(TestActionType.Click, timeout: TimeSpan.FromMilliseconds(120));

        var result = await runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), testCase, null, CancellationToken.None);

        Assert.Equal(TestExecutionStatus.Error, result.Status);
        Assert.Equal("automation_timeout", result.Steps[0].ErrorCode);
    }

    [Fact]
    public async Task Runner_StopsAfterFailureAndMarksFollowingStepsSkipped()
    {
        var driver = new FakeDriver
        {
            State = new AutomationElementState(true, true, false, false, "Salvar")
        };
        var runner = CreateRunner(driver);
        var first = CreateStep(1, TestActionType.AssertVisible, TimeSpan.FromMilliseconds(120), 1);
        var second = CreateStep(2, TestActionType.Click, TimeSpan.FromSeconds(1), 1);
        var testCase = new TestCaseDefinition(Guid.NewGuid(), "Fluxo", [first, second]);

        var result = await runner.RunAsync(Guid.NewGuid(), Guid.NewGuid(), testCase, null, CancellationToken.None);

        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(StepExecutionStatus.Skipped, result.Steps[1].Status);
        Assert.Equal(0, result.Steps[1].Attempts);
        Assert.Equal(0, driver.ClickCalls);
    }

    private static TestRunner CreateRunner(FakeDriver driver) =>
        new(driver, new ImmediateDelay(), NullLogger<TestRunner>.Instance);

    private static TestCaseDefinition CreateTestCase(
        TestActionType action,
        TimeSpan? timeout = null,
        int maxAttempts = 1) =>
        new(Guid.NewGuid(), "Caso", [CreateStep(1, action, timeout ?? TimeSpan.FromSeconds(1), maxAttempts)]);

    private static TestStepDefinition CreateStep(
        int sequence,
        TestActionType action,
        TimeSpan timeout,
        int maxAttempts) =>
        new(
            Guid.NewGuid(),
            sequence,
            action,
            new TestTarget([new TestLocator(TestLocatorKind.Name, "Salvar", 85)]),
            action == TestActionType.Input ? "QA Sentinel" : null,
            timeout,
            maxAttempts);

    private sealed class ImmediateDelay : IExecutionDelay
    {
        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, 5)), cancellationToken);
        }
    }

    private sealed class FakeDriver : ITestAutomationDriver
    {
        public AutomationElementState? State { get; init; }

        public int ClickFailuresRemaining { get; set; }

        public int ClickCalls { get; private set; }

        public Task<AutomationElementState?> FindAsync(
            Guid sessionId,
            TestTarget target,
            CancellationToken cancellationToken) => Task.FromResult(State);

        public Task ClickAsync(Guid sessionId, TestTarget target, CancellationToken cancellationToken)
        {
            ClickCalls++;
            if (ClickFailuresRemaining-- > 0)
            {
                throw new AutomationDriverException("element_busy", "Elemento ocupado.");
            }

            return Task.CompletedTask;
        }

        public Task FocusAsync(Guid sessionId, TestTarget target, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InputAsync(Guid sessionId, TestTarget target, string text, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
