using Microsoft.EntityFrameworkCore;
using QASentinel.Application.Executions;
using QASentinel.Domain.Entities;

namespace QASentinel.Infrastructure.Persistence;

internal sealed class TestExecutionRepository(IDbContextFactory<QASentinelDbContext> contextFactory)
    : ITestExecutionRepository
{
    public async Task<IReadOnlyList<TestExecution>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.TestExecutions
            .AsNoTracking()
            .Include(execution => execution.Steps)
            .OrderByDescending(execution => execution.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(TestExecution execution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.TestExecutions.Add(execution);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
