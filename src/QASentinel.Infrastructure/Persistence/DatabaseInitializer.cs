using Microsoft.EntityFrameworkCore;

namespace QASentinel.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IDbContextFactory<QASentinelDbContext> contextFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
