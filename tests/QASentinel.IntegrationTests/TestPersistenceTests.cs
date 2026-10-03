using Microsoft.EntityFrameworkCore;
using QASentinel.Domain.Entities;
using QASentinel.Domain.Testing;
using QASentinel.Infrastructure.Persistence;

namespace QASentinel.IntegrationTests;

public sealed class TestPersistenceTests
{
    [Fact]
    public async Task Sqlite_RoundTripsTestDefinitionAndExecutionResult()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"qasentinel-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<QASentinelDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        try
        {
            await using (var context = new QASentinelDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                var project = new Project("Projeto M2");
                var testCase = new TestCase(project.Id, "Clicar em Salvar");
                testCase.AddStep(
                    TestActionType.Click,
                    new TestTarget(
                    [
                        new TestLocator(TestLocatorKind.AutomationId, "btnSave", 100),
                        new TestLocator(TestLocatorKind.Name, "Salvar", 85)
                    ]),
                    timeout: TimeSpan.FromSeconds(2),
                    maxAttempts: 3);
                project.TestCases.Add(testCase);
                context.Projects.Add(project);
                await context.SaveChangesAsync();

                var execution = new TestExecution(
                    Guid.NewGuid(),
                    testCase.Id,
                    Guid.NewGuid(),
                    Environment.MachineName,
                    "1.0.0",
                    DateTimeOffset.UtcNow);
                var step = testCase.Steps.Single();
                execution.AddStep(new StepExecution(
                    execution.Id,
                    step.Id,
                    step.Sequence,
                    step.Action,
                    StepExecutionStatus.Passed,
                    1,
                    execution.StartedAt,
                    execution.StartedAt.AddMilliseconds(20)));
                execution.Complete(TestExecutionStatus.Passed, execution.StartedAt.AddMilliseconds(25));
                context.TestExecutions.Add(execution);
                await context.SaveChangesAsync();
            }

            await using (var context = new QASentinelDbContext(options))
            {
                var persistedCase = await context.TestCases.Include(testCase => testCase.Steps).SingleAsync();
                var persistedExecution = await context.TestExecutions.Include(execution => execution.Steps).SingleAsync();

                Assert.Equal("btnSave", persistedCase.Steps.Single().Target.Locators[0].Value);
                Assert.Equal(3, persistedCase.Steps.Single().MaxAttempts);
                Assert.Equal(TestExecutionStatus.Passed, persistedExecution.Status);
                Assert.Single(persistedExecution.Steps);
            }
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
