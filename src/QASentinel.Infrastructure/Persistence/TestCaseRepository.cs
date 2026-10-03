using Microsoft.EntityFrameworkCore;
using QASentinel.Application.Tests;
using QASentinel.Domain.Entities;

namespace QASentinel.Infrastructure.Persistence;

internal sealed class TestCaseRepository(IDbContextFactory<QASentinelDbContext> contextFactory) : ITestCaseRepository
{
    public async Task<IReadOnlyList<TestCase>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.TestCases
            .AsNoTracking()
            .Include(testCase => testCase.Steps)
            .OrderBy(testCase => testCase.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TestCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.TestCases
            .AsNoTracking()
            .Include(testCase => testCase.Steps)
            .SingleOrDefaultAsync(testCase => testCase.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(TestCase testCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.TestCases.Add(testCase);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
