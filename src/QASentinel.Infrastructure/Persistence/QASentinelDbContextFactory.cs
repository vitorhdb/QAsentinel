using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QASentinel.Infrastructure.Persistence;

public sealed class QASentinelDbContextFactory : IDesignTimeDbContextFactory<QASentinelDbContext>
{
    public QASentinelDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QASentinelDbContext>()
            .UseSqlite("Data Source=qasentinel.design.db")
            .Options;
        return new QASentinelDbContext(options);
    }
}
