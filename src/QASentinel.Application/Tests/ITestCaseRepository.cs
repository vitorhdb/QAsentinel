using QASentinel.Domain.Entities;

namespace QASentinel.Application.Tests;

public interface ITestCaseRepository
{
    Task<IReadOnlyList<TestCase>> GetAllAsync(CancellationToken cancellationToken);

    Task<TestCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(TestCase testCase, CancellationToken cancellationToken);
}
