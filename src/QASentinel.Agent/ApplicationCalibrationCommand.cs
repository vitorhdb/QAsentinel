using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;

namespace QASentinel.Agent;

public static class ApplicationCalibrationCommand
{
    private const string CommandArgument = "--calibrate-process";

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

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ApplicationCalibration");
        var highlighter = services.GetRequiredService<IElementHighlighter>();
        var windowStateService = services.GetRequiredService<IWindowStateService>();
        var restoreMinimizedState = false;
        var targetWindowHandle = nint.Zero;
        try
        {
            var processId = ReadRequiredInt(arguments, commandIndex + 1, CommandArgument, 1, int.MaxValue);
            var targetName = ReadRequiredOption(arguments, "--target-name");
            var targetClass = ReadOption(arguments, "--target-class");
            var outputDirectory = Path.GetFullPath(ReadOption(arguments, "--output")
                ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "calibration"));
            var timeoutSeconds = ReadOptionalInt(arguments, "--timeout-seconds", 30, 5, 120);
            var highlightMilliseconds = ReadOptionalInt(arguments, "--highlight-ms", 600, 100, 5_000);
            var invokeNavigation = HasFlag(arguments, "--invoke-navigation");
            var restoreTargetName = invokeNavigation
                ? ReadRequiredOption(arguments, "--restore-name")
                : null;
            var postNavigationTargetName = ReadOption(arguments, "--post-navigation-target-name");
            var postNavigationTargetClass = ReadOption(arguments, "--post-navigation-target-class");
            if (!string.IsNullOrWhiteSpace(postNavigationTargetName) && !invokeNavigation)
            {
                throw new ArgumentException("--post-navigation-target-name exige --invoke-navigation.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var engine = services.GetRequiredService<IAutomationEngine>();
            var screenshotService = services.GetRequiredService<IScreenshotService>();
            var session = await engine.AttachAsync(
                processId,
                TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 15)),
                timeout.Token).ConfigureAwait(false);
            targetWindowHandle = session.MainWindowHandle;
            var initialWindowState = windowStateService.GetState(targetWindowHandle);
            if (initialWindowState == WindowPresentationState.Minimized)
            {
                windowStateService.Restore(targetWindowHandle);
                restoreMinimizedState = true;
                await WaitUntilRestoredAsync(windowStateService, targetWindowHandle, timeout.Token)
                    .ConfigureAwait(false);
            }

            var activationSucceeded = windowStateService.Activate(targetWindowHandle);
            var scan = await engine.ScanCompatibilityAsync(
                session.Id,
                new CompatibilityScanOptions(MaxDepth: 16, MaxElements: 5_000),
                timeout.Token).ConfigureAwait(false);
            var target = SelectUniqueTarget(scan, targetName, targetClass);
            var resolved = await engine.FindElementAsync(session.Id, target.Locators, timeout.Token)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("O elemento selecionado não foi resolvido novamente.");
            EnsureEquivalent(target, resolved);

            await engine.FocusAsync(session.Id, target.Locators, timeout.Token).ConfigureAwait(false);
            var afterFocus = await engine.FindElementAsync(session.Id, target.Locators, timeout.Token)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("O elemento não foi encontrado após receber foco.");
            EnsureEquivalent(target, afterFocus);

            ScreenshotResult applicationScreenshot;
            ScreenshotResult elementScreenshot;
            await highlighter.ShowAsync(afterFocus.Bounds, timeout.Token).ConfigureAwait(false);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token).ConfigureAwait(false);
                applicationScreenshot = await screenshotService.CaptureAsync(
                    ScreenshotTarget.Application,
                    session,
                    null,
                    outputDirectory,
                    timeout.Token).ConfigureAwait(false);
                elementScreenshot = await screenshotService.CaptureAsync(
                    ScreenshotTarget.Element,
                    session,
                    afterFocus,
                    outputDirectory,
                    timeout.Token).ConfigureAwait(false);
                var remainingHighlight = Math.Max(0, highlightMilliseconds - 150);
                if (remainingHighlight > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remainingHighlight), timeout.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                await highlighter.HideAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var navigationInvoked = false;
            var navigationRestored = false;
            int? elementsAfterNavigation = null;
            ScreenshotResult? navigationScreenshot = null;
            ScreenshotResult? restoredScreenshot = null;
            ScreenshotResult? postNavigationElementScreenshot = null;
            AutomationElementInfo? postNavigationTarget = null;
            AutomationElementInfo? postNavigationTargetAfterFocus = null;
            if (invokeNavigation)
            {
                await engine.ClickAsync(session.Id, target.Locators, timeout.Token).ConfigureAwait(false);
                navigationInvoked = true;
                await Task.Delay(TimeSpan.FromMilliseconds(900), timeout.Token).ConfigureAwait(false);

                var navigationScan = await engine.ScanCompatibilityAsync(
                    session.Id,
                    new CompatibilityScanOptions(MaxDepth: 16, MaxElements: 5_000),
                    timeout.Token).ConfigureAwait(false);
                elementsAfterNavigation = navigationScan.Elements.Count;
                if (!string.IsNullOrWhiteSpace(postNavigationTargetName))
                {
                    postNavigationTarget = SelectUniqueTarget(
                        navigationScan,
                        postNavigationTargetName,
                        postNavigationTargetClass);
                    var postNavigationResolved = await engine.FindElementAsync(
                        session.Id,
                        postNavigationTarget.Locators,
                        timeout.Token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("O alvo pós-navegação não foi resolvido novamente.");
                    EnsureEquivalent(postNavigationTarget, postNavigationResolved);
                    await engine.FocusAsync(session.Id, postNavigationTarget.Locators, timeout.Token)
                        .ConfigureAwait(false);
                    postNavigationTargetAfterFocus = await engine.FindElementAsync(
                        session.Id,
                        postNavigationTarget.Locators,
                        timeout.Token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("O alvo pós-navegação não foi encontrado após o foco.");
                    EnsureEquivalent(postNavigationTarget, postNavigationTargetAfterFocus);

                    await highlighter.ShowAsync(postNavigationTargetAfterFocus.Bounds, timeout.Token)
                        .ConfigureAwait(false);
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token).ConfigureAwait(false);
                        navigationScreenshot = await screenshotService.CaptureAsync(
                            ScreenshotTarget.Application,
                            session,
                            null,
                            outputDirectory,
                            timeout.Token).ConfigureAwait(false);
                        postNavigationElementScreenshot = await screenshotService.CaptureAsync(
                            ScreenshotTarget.Element,
                            session,
                            postNavigationTargetAfterFocus,
                            outputDirectory,
                            timeout.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await highlighter.HideAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                else
                {
                    navigationScreenshot = await screenshotService.CaptureAsync(
                        ScreenshotTarget.Application,
                        session,
                        null,
                        outputDirectory,
                        timeout.Token).ConfigureAwait(false);
                }

                var restoreTarget = SelectUniqueTarget(navigationScan, restoreTargetName!, targetClass: null);
                await engine.ClickAsync(session.Id, restoreTarget.Locators, timeout.Token).ConfigureAwait(false);
                navigationRestored = true;
                await Task.Delay(TimeSpan.FromMilliseconds(900), timeout.Token).ConfigureAwait(false);
                restoredScreenshot = await screenshotService.CaptureAsync(
                    ScreenshotTarget.Application,
                    session,
                    null,
                    outputDirectory,
                    timeout.Token).ConfigureAwait(false);
            }

            var reportPath = await WriteReportAsync(
                session,
                target,
                afterFocus,
                applicationScreenshot,
                elementScreenshot,
                initialWindowState,
                activationSucceeded,
                navigationInvoked,
                navigationRestored,
                restoreTargetName,
                elementsAfterNavigation,
                postNavigationTarget,
                postNavigationTargetAfterFocus,
                navigationScreenshot,
                postNavigationElementScreenshot,
                restoredScreenshot,
                outputDirectory,
                timeout.Token).ConfigureAwait(false);

            logger.LogInformation(
                "Operation=ApplicationCalibration ProcessId={ProcessId} Element={Element} ClassName={ClassName} " +
                "Focused={Focused} Report={ReportPath} Result=Success",
                processId,
                target.Name,
                target.ClassName,
                afterFocus.IsFocused,
                reportPath);
            return 0;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogError(exception, "Operation=ApplicationCalibration Result=Timeout");
            return 2;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=ApplicationCalibration Result=Failure");
            return 1;
        }
        finally
        {
            try
            {
                await highlighter.HideAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (restoreMinimizedState && targetWindowHandle != nint.Zero)
                {
                    windowStateService.Minimize(targetWindowHandle);
                }
            }
        }
    }

    private static AutomationElementInfo SelectUniqueTarget(
        AutomationCompatibilityScan scan,
        string targetName,
        string? targetClass)
    {
        var candidates = scan.Elements
            .Select(item => item.Element)
            .Where(element => string.Equals(element.Name, targetName, StringComparison.OrdinalIgnoreCase))
            .Where(element => string.IsNullOrWhiteSpace(targetClass) ||
                              string.Equals(element.ClassName, targetClass, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"Nenhum elemento corresponde a Name='{targetName}' e ClassName='{targetClass ?? "*"}'."),
            _ => throw new InvalidOperationException(
                $"A calibração exige um alvo único; {candidates.Length} elementos correspondem a Name='{targetName}'.")
        };
    }

    private static void EnsureEquivalent(AutomationElementInfo expected, AutomationElementInfo actual)
    {
        if (!string.Equals(expected.Name, actual.Name, StringComparison.Ordinal) ||
            !string.Equals(expected.ClassName, actual.ClassName, StringComparison.Ordinal) ||
            !string.Equals(expected.ControlType, actual.ControlType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"O LocatorSet resolveu um elemento diferente. " +
                $"Esperado='{expected.Name}|{expected.ClassName}|{expected.ControlType}', " +
                $"Obtido='{actual.Name}|{actual.ClassName}|{actual.ControlType}'.");
        }
    }

    private static async Task<string> WriteReportAsync(
        ApplicationSession session,
        AutomationElementInfo target,
        AutomationElementInfo afterFocus,
        ScreenshotResult applicationScreenshot,
        ScreenshotResult elementScreenshot,
        WindowPresentationState initialWindowState,
        bool activationSucceeded,
        bool navigationInvoked,
        bool navigationRestored,
        string? restoreTargetName,
        int? elementsAfterNavigation,
        AutomationElementInfo? postNavigationTarget,
        AutomationElementInfo? postNavigationTargetAfterFocus,
        ScreenshotResult? navigationScreenshot,
        ScreenshotResult? postNavigationElementScreenshot,
        ScreenshotResult? restoredScreenshot,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var timestamp = DateTimeOffset.Now;
        var fileName = $"{timestamp:yyyyMMdd_HHmmss}_{SanitizeFileName(session.ProcessName)}_calibration.json";
        var filePath = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
        var report = new
        {
            capturedAt = timestamp,
            operationMode = navigationInvoked ? "controlledNavigation" : "nonDestructive",
            initialWindowState,
            restoredTemporarily = initialWindowState == WindowPresentationState.Minimized,
            activationSucceeded,
            session = new
            {
                session.ProcessId,
                session.ProcessName,
                session.ExecutablePath,
                mainWindowHandle = session.MainWindowHandle.ToInt64(),
                session.WindowTitle,
                backend = session.Backend.ToString()
            },
            target = new
            {
                target.Name,
                target.AutomationId,
                target.ClassName,
                target.ControlType,
                target.FrameworkId,
                nativeWindowHandle = target.NativeWindowHandle.ToInt64(),
                target.Bounds,
                target.SupportedPatterns,
                locators = target.Locators.Locators
            },
            postNavigationTarget = postNavigationTarget is null
                ? null
                : new
                {
                    postNavigationTarget.Name,
                    postNavigationTarget.AutomationId,
                    postNavigationTarget.ClassName,
                    postNavigationTarget.ControlType,
                    postNavigationTarget.FrameworkId,
                    nativeWindowHandle = postNavigationTarget.NativeWindowHandle.ToInt64(),
                    postNavigationTarget.Bounds,
                    postNavigationTarget.SupportedPatterns,
                    locators = postNavigationTarget.Locators.Locators
                },
            checks = new
            {
                locatorRoundTrip = true,
                semanticIdentityPreserved = true,
                focusRequested = true,
                focusedAfterRequest = afterFocus.IsFocused,
                targetVisibleAfterRestore = !afterFocus.IsOffscreen && !afterFocus.Bounds.IsEmpty,
                highlightRequested = true,
                highlightScreenshotCaptured = true,
                navigationInvoked,
                navigationRestored,
                restoreTargetName,
                elementsAfterNavigation,
                postNavigationTargetResolved = postNavigationTarget is not null,
                postNavigationTargetFocused = postNavigationTargetAfterFocus?.IsFocused,
                postNavigationHighlightCaptured = postNavigationElementScreenshot is not null,
                textEntered = false
            },
            evidence = new
            {
                applicationScreenshot,
                elementScreenshot,
                navigationScreenshot,
                postNavigationElementScreenshot,
                restoredScreenshot
            }
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return filePath;
    }

    private static async Task WaitUntilRestoredAsync(
        IWindowStateService windowStateService,
        nint windowHandle,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (windowStateService.GetState(windowHandle) != WindowPresentationState.Minimized)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("A janela alvo não saiu do estado minimizado em três segundos.");
    }

    private static int FindOption(string[] arguments, string option) =>
        Array.FindIndex(arguments, argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

    private static bool HasFlag(string[] arguments, string option) => FindOption(arguments, option) >= 0;

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

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "application" : sanitized;
    }
}
