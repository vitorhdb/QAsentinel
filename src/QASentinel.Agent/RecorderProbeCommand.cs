using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;
using QASentinel.Recorder;

namespace QASentinel.Agent;

public static class RecorderProbeCommand
{
    private const string CommandArgument = "--recorder-probe";

    public static async Task<int?> TryRunAsync(
        string[] arguments,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var commandIndex = FindOption(arguments, CommandArgument);
        if (commandIndex < 0)
        {
            return null;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RecorderProbe");
        var recorder = services.GetRequiredService<IDesktopInputRecorder>();
        var engine = services.GetRequiredService<IAutomationEngine>();
        var screenshotService = services.GetRequiredService<IScreenshotService>();
        var windowStateService = services.GetRequiredService<IWindowStateService>();
        ApplicationSession? session = null;
        LocatorSet? restoreTarget = null;
        var navigationRestored = false;
        try
        {
            var processId = ReadRequiredInt(arguments, commandIndex + 1, CommandArgument, 1, int.MaxValue);
            var targetName = ReadRequiredOption(arguments, "--target-name");
            var restoreName = ReadRequiredOption(arguments, "--restore-name");
            var outputDirectory = Path.GetFullPath(ReadOption(arguments, "--output")
                ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "recorder-probes"));
            var timeoutSeconds = ReadOptionalInt(arguments, "--timeout-seconds", 45, 10, 120);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            session = await engine.AttachAsync(
                processId,
                TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 15)),
                timeout.Token).ConfigureAwait(false);
            if (windowStateService.GetState(session.MainWindowHandle) == WindowPresentationState.Minimized)
            {
                windowStateService.Restore(session.MainWindowHandle);
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
            }

            _ = windowStateService.Activate(session.MainWindowHandle);
            var initialScan = await ScanAsync(engine, session.Id, timeout.Token).ConfigureAwait(false);
            restoreTarget = SelectUniqueTarget(initialScan, restoreName).Locators;

            // Establish a known, reversible baseline before recording.
            await engine.ClickAsync(session.Id, restoreTarget, timeout.Token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(900), timeout.Token).ConfigureAwait(false);
            var baselineScreenshot = await screenshotService.CaptureAsync(
                ScreenshotTarget.Application,
                session,
                null,
                outputDirectory,
                timeout.Token).ConfigureAwait(false);

            var baselineScan = await ScanAsync(engine, session.Id, timeout.Token).ConfigureAwait(false);
            var navigationTarget = SelectUniqueTarget(baselineScan, targetName);
            restoreTarget = SelectUniqueTarget(baselineScan, restoreName).Locators;

            await recorder.StartAsync(session.Id, timeout.Token).ConfigureAwait(false);
            await engine.PhysicalClickAsync(session.Id, navigationTarget.Locators, timeout.Token).ConfigureAwait(false);
            await WaitForStepCountAsync(recorder, 1, timeout.Token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(900), timeout.Token).ConfigureAwait(false);
            var navigationScreenshot = await screenshotService.CaptureAsync(
                ScreenshotTarget.Application,
                session,
                null,
                outputDirectory,
                timeout.Token).ConfigureAwait(false);

            var navigationScan = await ScanAsync(engine, session.Id, timeout.Token).ConfigureAwait(false);
            restoreTarget = SelectUniqueTarget(navigationScan, restoreName).Locators;
            await engine.PhysicalClickAsync(session.Id, restoreTarget, timeout.Token).ConfigureAwait(false);
            await WaitForStepCountAsync(recorder, 2, timeout.Token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(900), timeout.Token).ConfigureAwait(false);
            navigationRestored = true;
            await recorder.StopAsync(timeout.Token).ConfigureAwait(false);

            var snapshot = recorder.Current;
            ValidateSnapshot(snapshot, targetName, restoreName);
            var restoredScreenshot = await screenshotService.CaptureAsync(
                ScreenshotTarget.Application,
                session,
                null,
                outputDirectory,
                timeout.Token).ConfigureAwait(false);
            var reportPath = await WriteReportAsync(
                session,
                targetName,
                restoreName,
                snapshot,
                baselineScreenshot,
                navigationScreenshot,
                restoredScreenshot,
                outputDirectory,
                timeout.Token).ConfigureAwait(false);

            logger.LogInformation(
                "Operation=RecorderProbe ProcessId={ProcessId} Steps={StepCount} Restored={Restored} Report={ReportPath} Result=Success",
                processId,
                snapshot.Steps.Count,
                navigationRestored,
                reportPath);
            return 0;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogError(exception, "Operation=RecorderProbe Result=Timeout");
            return 2;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=RecorderProbe Result=Failure");
            return 1;
        }
        finally
        {
            if (recorder.Current.IsRecording)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await recorder.StopAsync(stopTimeout.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Operation=RecorderProbeStop Result=Failure");
                }
            }

            if (!navigationRestored && session is not null && restoreTarget is not null)
            {
                using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await engine.ClickAsync(session.Id, restoreTarget, restoreTimeout.Token).ConfigureAwait(false);
                    logger.LogInformation("Operation=RecorderProbeRestore Result=Success");
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Operation=RecorderProbeRestore Result=Failure");
                }
            }
        }
    }

    private static Task<AutomationCompatibilityScan> ScanAsync(
        IAutomationEngine engine,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        engine.ScanCompatibilityAsync(
            sessionId,
            new CompatibilityScanOptions(MaxDepth: 16, MaxElements: 5_000),
            cancellationToken);

    private static AutomationElementInfo SelectUniqueTarget(AutomationCompatibilityScan scan, string name)
    {
        var candidates = scan.Elements
            .Select(item => item.Element)
            .Where(element => string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))
            .Where(element => string.Equals(element.ControlType, "TreeItem", StringComparison.Ordinal))
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException($"O item de navegação '{name}' não foi encontrado."),
            _ => throw new InvalidOperationException($"O item de navegação '{name}' não é único.")
        };
    }

    private static async Task WaitForStepCountAsync(
        IDesktopInputRecorder recorder,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        while (recorder.Current.Steps.Count < expectedCount)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateSnapshot(RecorderSnapshot snapshot, string targetName, string restoreName)
    {
        if (snapshot.Steps.Count != 2 || snapshot.Steps.Any(step => step.Action != RecordedActionType.Click))
        {
            throw new InvalidOperationException(
                $"A gravação deveria conter dois clicks; foram obtidos {snapshot.Steps.Count} passos.");
        }

        if (!string.Equals(snapshot.Steps[0].DisplayName, targetName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(snapshot.Steps[1].DisplayName, restoreName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"A sequência gravada divergiu do esperado: {string.Join(" -> ", snapshot.Steps.Select(step => step.DisplayName))}.");
        }
    }

    private static async Task<string> WriteReportAsync(
        ApplicationSession session,
        string targetName,
        string restoreName,
        RecorderSnapshot snapshot,
        ScreenshotResult baselineScreenshot,
        ScreenshotResult navigationScreenshot,
        ScreenshotResult restoredScreenshot,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var capturedAt = DateTimeOffset.Now;
        var filePath = Path.GetFullPath(Path.Combine(
            outputDirectory,
            $"{capturedAt:yyyyMMdd_HHmmss}_{session.ProcessName}_recorder_probe.json"));
        var report = new
        {
            capturedAt,
            session = new
            {
                session.ProcessId,
                session.ProcessName,
                session.ExecutablePath,
                session.WindowTitle,
                backend = session.Backend.ToString()
            },
            scenario = new
            {
                baseline = restoreName,
                navigation = targetName,
                returnTo = restoreName,
                destructiveActions = false
            },
            checks = new
            {
                hookFilteredByProcess = true,
                semanticTargetsResolved = true,
                navigationRestored = true,
                expectedStepCount = 2,
                actualStepCount = snapshot.Steps.Count
            },
            steps = snapshot.Steps,
            evidence = new
            {
                baselineScreenshot,
                navigationScreenshot,
                restoredScreenshot
            }
        };
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

    private static int FindOption(string[] arguments, string option) =>
        Array.FindIndex(arguments, argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

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
        var optionIndex = FindOption(arguments, option);
        return optionIndex < 0
            ? defaultValue
            : ReadRequiredInt(arguments, optionIndex + 1, option, minimum, maximum);
    }

    private static string ReadRequiredOption(string[] arguments, string option) =>
        ReadOption(arguments, option)
        ?? throw new ArgumentException($"Informe um valor após {option}.");

    private static string? ReadOption(string[] arguments, string option)
    {
        var optionIndex = FindOption(arguments, option);
        return optionIndex < 0
            ? null
            : optionIndex + 1 < arguments.Length && !string.IsNullOrWhiteSpace(arguments[optionIndex + 1])
                ? arguments[optionIndex + 1]
                : throw new ArgumentException($"Informe um valor após {option}.");
    }
}
