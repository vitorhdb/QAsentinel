using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QASentinel.Application.Executions;
using QASentinel.Automation.Abstractions;
using QASentinel.Domain.Testing;

namespace QASentinel.Agent;

public static class RunnerProbeCommand
{
    private const string CommandArgument = "--runner-probe";

    public static async Task<int?> TryRunAsync(
        string[] arguments,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var commandIndex = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, CommandArgument, StringComparison.OrdinalIgnoreCase));
        if (commandIndex < 0)
        {
            return null;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RunnerProbe");
        try
        {
            var processId = ReadRequiredInt(arguments, commandIndex + 1, CommandArgument, 1, int.MaxValue);
            var target = CreateTarget(arguments);
            var timeoutSeconds = ReadOptionalInt(arguments, "--timeout-seconds", 30, 5, 120);
            var outputDirectory = Path.GetFullPath(ReadOption(arguments, "--output")
                ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "runner-probes"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var engine = services.GetRequiredService<IAutomationEngine>();
            var session = await engine.AttachAsync(
                processId,
                TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 15)),
                timeout.Token).ConfigureAwait(false);
            var runner = services.GetRequiredService<ITestRunner>();
            var definition = CreateDefinition(target);
            var result = await runner.RunAsync(
                Guid.NewGuid(),
                session.Id,
                definition,
                null,
                timeout.Token).ConfigureAwait(false);
            var reportPath = await WriteReportAsync(
                new RunnerProbeReport(DateTimeOffset.Now, processId, session.WindowTitle, target, result),
                outputDirectory,
                timeout.Token).ConfigureAwait(false);

            logger.LogInformation(
                "Operation=RunnerProbe ProcessId={ProcessId} Status={Status} Steps={StepCount} Report={ReportPath} Result={Result}",
                processId,
                result.Status,
                result.Steps.Count,
                reportPath,
                result.Status == TestExecutionStatus.Passed ? "Success" : "Failure");
            return result.Status == TestExecutionStatus.Passed ? 0 : 1;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogError(exception, "Operation=RunnerProbe Result=Timeout");
            return 2;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=RunnerProbe Result=Failure");
            return 1;
        }
    }

    private static TestCaseDefinition CreateDefinition(TestTarget target)
    {
        var timeout = TimeSpan.FromSeconds(5);
        var actions = new[]
        {
            TestActionType.WaitForElement,
            TestActionType.AssertExists,
            TestActionType.AssertVisible,
            TestActionType.AssertEnabled
        };
        return new TestCaseDefinition(
            Guid.NewGuid(),
            "Argus read-only runner probe",
            actions.Select((action, index) => new TestStepDefinition(
                Guid.NewGuid(),
                index + 1,
                action,
                target,
                null,
                timeout,
                1)).ToArray());
    }

    private static TestTarget CreateTarget(string[] arguments)
    {
        var locators = new List<TestLocator>();
        AddLocator(locators, TestLocatorKind.AutomationId, ReadOption(arguments, "--target-automation-id"), 100);
        AddLocator(locators, TestLocatorKind.Name, ReadOption(arguments, "--target-name"), 85);
        AddLocator(locators, TestLocatorKind.ClassName, ReadOption(arguments, "--target-class"), 70);
        AddLocator(locators, TestLocatorKind.ControlType, ReadOption(arguments, "--target-control-type"), 65);
        if (locators.Count == 0)
        {
            throw new ArgumentException(
                "Informe ao menos um locator: --target-automation-id, --target-name, --target-class ou --target-control-type.");
        }

        return new TestTarget(locators);
    }

    private static void AddLocator(
        ICollection<TestLocator> locators,
        TestLocatorKind kind,
        string? value,
        int score)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            locators.Add(new TestLocator(kind, value, score));
        }
    }

    private static async Task<string> WriteReportAsync(
        RunnerProbeReport report,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_Argus_runner_probe.json";
        var filePath = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(report, options),
            cancellationToken).ConfigureAwait(false);
        return filePath;
    }

    private static string? ReadOption(string[] arguments, string option)
    {
        var index = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }

    private static int ReadRequiredInt(
        string[] arguments,
        int index,
        string option,
        int minimum,
        int maximum)
    {
        if (index >= arguments.Length ||
            !int.TryParse(arguments[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new ArgumentException($"Informe um valor entre {minimum} e {maximum} após {option}.");
        }

        return value;
    }

    private static int ReadOptionalInt(
        string[] arguments,
        string option,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var value = ReadOption(arguments, option);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new ArgumentException($"Informe um valor entre {minimum} e {maximum} após {option}.");
        }

        return parsed;
    }

    private sealed record RunnerProbeReport(
        DateTimeOffset CapturedAt,
        int ProcessId,
        string WindowTitle,
        TestTarget Target,
        TestRunResult Result);
}
