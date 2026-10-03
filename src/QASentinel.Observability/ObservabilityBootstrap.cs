using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace QASentinel.Observability;

public static class ObservabilityBootstrap
{
    public static HostApplicationBuilder AddQASentinelObservability(
        this HostApplicationBuilder builder,
        string component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QASentinel",
            "logs");
        Directory.CreateDirectory(logsDirectory);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.WithProperty("Component", component)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Component} {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logsDirectory, $"{component}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:O} [{Level:u3}] Component={Component} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Services.AddSerilog(logger, dispose: true);
        return builder;
    }
}
