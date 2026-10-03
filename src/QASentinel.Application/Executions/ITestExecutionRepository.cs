using QASentinel.Domain.Entities;

namespace QASentinel.Application.Executions;

public interface ITestExecutionRepository
{
    Task<IReadOnlyList<TestExecution>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task AddAsync(TestExecution execution, CancellationToken cancellationToken);
}
