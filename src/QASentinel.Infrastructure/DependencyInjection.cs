using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QASentinel.Application.Projects;
using QASentinel.Application.Executions;
using QASentinel.Application.Tests;
using QASentinel.Infrastructure.Persistence;

namespace QASentinel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? databasePath = null)
    {
        var resolvedPath = databasePath ?? GetDefaultDatabasePath();
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContextFactory<QASentinelDbContext>(options =>
            options.UseSqlite($"Data Source={resolvedPath}"));
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<ITestCaseRepository, TestCaseRepository>();
        services.AddSingleton<ITestExecutionRepository, TestExecutionRepository>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }

    public static string GetDefaultDatabasePath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localData, "QASentinel", "qasentinel.db");
    }
}
