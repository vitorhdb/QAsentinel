using QASentinel.Domain.Entities;
using QASentinel.Domain.Testing;

namespace QASentinel.UnitTests;

public sealed class TestDomainTests
{
    [Fact]
    public void TestCase_AddStepAssignsSequenceAndNormalizesTarget()
    {
        var testCase = new TestCase(Guid.NewGuid(), "  Cadastrar grupo  ");
        var target = new TestTarget(
        [
            new TestLocator(TestLocatorKind.Name, " Salvar ", 85),
            new TestLocator(TestLocatorKind.Name, "Salvar", 80),
            new TestLocator(TestLocatorKind.ClassName, "TButton", 70)
        ]);

        var step = testCase.AddStep(TestActionType.Click, target, timeout: TimeSpan.FromSeconds(2), maxAttempts: 3);

        Assert.Equal("Cadastrar grupo", testCase.Name);
        Assert.Equal(1, step.Sequence);
        Assert.Equal(2, step.Target.Locators.Count);
        Assert.Equal(85, step.Target.Locators[0].Score);
        Assert.Equal(3, step.MaxAttempts);
    }

    [Fact]
    public void TestStep_InputRequiresText()
    {
        var testCase = new TestCase(Guid.NewGuid(), "Preencher descrição");
        var target = new TestTarget([new TestLocator(TestLocatorKind.AutomationId, "txtDescription", 100)]);

        Assert.Throws<ArgumentException>(() => testCase.AddStep(TestActionType.Input, target));
    }

    [Fact]
    public void TestExecution_KeepsStepResultsAndFinalStatus()
    {
        var executionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var execution = new TestExecution(
            executionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "QA-PC",
            "1.0.0",
            startedAt);
        execution.AddStep(new StepExecution(
            executionId,
            Guid.NewGuid(),
            1,
            TestActionType.AssertVisible,
            StepExecutionStatus.Passed,
            1,
            startedAt,
            startedAt.AddMilliseconds(25)));

        execution.Complete(TestExecutionStatus.Passed, startedAt.AddMilliseconds(30));

        Assert.Equal(TestExecutionStatus.Passed, execution.Status);
        Assert.Single(execution.Steps);
        Assert.Equal(30, execution.DurationMilliseconds);
    }
}
