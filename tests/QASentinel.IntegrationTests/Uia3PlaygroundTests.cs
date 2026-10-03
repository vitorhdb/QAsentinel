using System.Diagnostics;
using System.Drawing;
using FlaUI.Core.Input;
using Microsoft.Extensions.Logging.Abstractions;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;
using QASentinel.Automation.UIA;
using QASentinel.Automation.Win32;
using QASentinel.Recorder;

namespace QASentinel.IntegrationTests;

public sealed class Uia3PlaygroundTests
{
    [Fact]
    [Trait("Category", "DesktopIntegration")]
    public async Task Engine_AttachesFindsInputsTypesAndCapturesScreenshot()
    {
        var playground = ResolvePlaygroundPath();
        using var process = Process.Start(new ProcessStartInfo(playground) { UseShellExecute = true })
            ?? throw new InvalidOperationException("Não foi possível iniciar o Test Playground.");

        try
        {
            process.WaitForInputIdle(10_000);
            await Task.Delay(750);
            await using var engine = new Uia3AutomationEngine(
                new WindowsTopLevelWindowService(),
                new WindowsWindowStateService(),
                NullLogger<Uia3AutomationEngine>.Instance);
            var session = await engine.AttachAsync(process.Id, TimeSpan.FromSeconds(10), CancellationToken.None);
            var textBoxLocators = new LocatorSet(
            [
                new ElementLocator(LocatorKind.AutomationId, "txtName"),
                new ElementLocator(LocatorKind.Name, "Nome")
            ]);
            var element = await FindElementEventuallyAsync(engine, session.Id, textBoxLocators);
            var compatibilityScan = await engine.ScanCompatibilityAsync(
                session.Id,
                new CompatibilityScanOptions(MaxDepth: 10, MaxElements: 500),
                CancellationToken.None);
            Assert.NotEmpty(compatibilityScan.Elements);
            Assert.Contains(
                compatibilityScan.Elements,
                item => string.Equals(item.Element.AutomationId, "txtName", StringComparison.Ordinal));
            Assert.False(compatibilityScan.ElementLimitReached);
            Assert.All(
                compatibilityScan.Elements.Where(item => item.Element.NativeWindowHandle == nint.Zero),
                item => Assert.DoesNotContain(
                    item.Element.Locators.Locators,
                    locator => locator.Kind == LocatorKind.NativeWindowHandle));
            Assert.All(
                compatibilityScan.Elements.Where(item =>
                    item.Element.NativeWindowHandle != nint.Zero &&
                    long.TryParse(item.Element.AutomationId, out var automationId) &&
                    automationId == item.Element.NativeWindowHandle.ToInt64()),
                item => Assert.DoesNotContain(
                    item.Element.Locators.Locators,
                    locator => locator.Kind == LocatorKind.AutomationId));

            Assert.Equal("Edit", element.ControlType);

            await engine.TypeTextAsync(session.Id, textBoxLocators, "QA Sentinel integration", CancellationToken.None);

            var outputDirectory = Path.Combine(Path.GetTempPath(), "QASentinel.IntegrationTests", Guid.NewGuid().ToString("N"));
            var screenshotService = new WindowsScreenshotService();
            var screenshot = await screenshotService.CaptureAsync(
                ScreenshotTarget.Element,
                session,
                element,
                outputDirectory,
                CancellationToken.None);
            var secondScreenshot = await screenshotService.CaptureAsync(
                ScreenshotTarget.Element,
                session,
                element,
                outputDirectory,
                CancellationToken.None);

            Assert.True(File.Exists(screenshot.FilePath));
            Assert.True(File.Exists(secondScreenshot.FilePath));
            Assert.NotEqual(screenshot.FilePath, secondScreenshot.FilePath);
            Assert.True(screenshot.Width > 0);
            Assert.True(screenshot.Height > 0);

            var dataTabLocators = new LocatorSet(
            [
                new ElementLocator(LocatorKind.AutomationId, "tabData"),
                new ElementLocator(LocatorKind.Name, "Dados")
            ]);
            await engine.ClickAsync(session.Id, dataTabLocators, CancellationToken.None);
            var grid = await engine.FindElementAsync(
                session.Id,
                new LocatorSet([new ElementLocator(LocatorKind.AutomationId, "gridRecords")]),
                CancellationToken.None);
            Assert.NotNull(grid);
            Assert.False(grid.IsOffscreen);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact]
    [Trait("Category", "DesktopIntegration")]
    public async Task Recorder_CapturesSemanticClickAndReplaysItThroughAutomationEngine()
    {
        var playground = ResolvePlaygroundPath();
        using var process = Process.Start(new ProcessStartInfo(playground) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Não foi possível iniciar o Test Playground.");

        var windowState = new WindowsWindowStateService();
        var engine = new Uia3AutomationEngine(
            new WindowsTopLevelWindowService(),
            windowState,
            NullLogger<Uia3AutomationEngine>.Instance);
        var recorder = new DesktopInputRecorder(
            new WindowsMouseHookSource(),
            engine,
            NullLogger<DesktopInputRecorder>.Instance);

        try
        {
            process.WaitForInputIdle(10_000);
            await Task.Delay(750);
            var session = await engine.AttachAsync(process.Id, TimeSpan.FromSeconds(10), CancellationToken.None);
            var clearTarget = new LocatorSet([new ElementLocator(LocatorKind.AutomationId, "btnClear")]);
            var clearButton = await FindElementEventuallyAsync(
                engine,
                session.Id,
                clearTarget);

            windowState.Restore(session.MainWindowHandle);
            _ = windowState.Activate(session.MainWindowHandle);
            await engine.FocusAsync(
                session.Id,
                clearTarget,
                CancellationToken.None);
            await Task.Delay(250);
            await recorder.StartAsync(session.Id, CancellationToken.None);
            Mouse.LeftClick(new Point(
                (int)Math.Round(clearButton.Bounds.X + (clearButton.Bounds.Width / 2)),
                (int)Math.Round(clearButton.Bounds.Y + (clearButton.Bounds.Height / 2))));

            using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (recorder.Current.Steps.Count == 0)
            {
                await Task.Delay(50, captureTimeout.Token);
            }

            await recorder.StopAsync(CancellationToken.None);
            var recordedStep = Assert.Single(recorder.Current.Steps);
            Assert.Equal(RecordedActionType.Click, recordedStep.Action);
            Assert.Contains(
                recordedStep.Target.Locators,
                locator => locator.Kind == LocatorKind.AutomationId && locator.Value == "btnClear");

            await engine.ClickAsync(session.Id, recordedStep.Target, CancellationToken.None);
            var lastAction = await engine.FindElementAsync(
                session.Id,
                new LocatorSet([new ElementLocator(LocatorKind.AutomationId, "lblLastAction")]),
                CancellationToken.None);
            Assert.NotNull(lastAction);
            Assert.Equal("Formulário limpo", lastAction.Name);
        }
        finally
        {
            if (recorder.Current.IsRecording)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await recorder.StopAsync(stopTimeout.Token);
            }

            await recorder.DisposeAsync();
            await engine.DisposeAsync();

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact]
    [Trait("Category", "DesktopIntegration")]
    public async Task Recorder_CapturesSemanticFillAndNeverStoresPasswordContent()
    {
        const string safeValue = "QA Sentinel semantic fill";
        const string passwordValue = "dummy-secret-never-record";
        var playground = ResolvePlaygroundPath();
        using var process = Process.Start(new ProcessStartInfo(playground) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Não foi possível iniciar o Test Playground.");

        var windowState = new WindowsWindowStateService();
        var engine = new Uia3AutomationEngine(
            new WindowsTopLevelWindowService(),
            windowState,
            NullLogger<Uia3AutomationEngine>.Instance);
        var recorder = new DesktopInputRecorder(
            new WindowsMouseHookSource(),
            engine,
            NullLogger<DesktopInputRecorder>.Instance);

        try
        {
            process.WaitForInputIdle(10_000);
            await Task.Delay(750);
            var session = await engine.AttachAsync(process.Id, TimeSpan.FromSeconds(10), CancellationToken.None);
            var nameTarget = new LocatorSet([new ElementLocator(LocatorKind.AutomationId, "txtName")]);
            var passwordTarget = new LocatorSet([new ElementLocator(LocatorKind.AutomationId, "txtPassword")]);
            _ = await FindElementEventuallyAsync(engine, session.Id, nameTarget);
            _ = await FindElementEventuallyAsync(engine, session.Id, passwordTarget);
            windowState.Restore(session.MainWindowHandle);
            _ = windowState.Activate(session.MainWindowHandle);

            await recorder.StartAsync(session.Id, CancellationToken.None);
            await engine.PhysicalClickAsync(session.Id, nameTarget, CancellationToken.None);
            await WaitUntilAsync(
                () => recorder.Current.StatusMessage.StartsWith("Monitorando preenchimento", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5),
                () => DescribeRecorder(recorder));
            await engine.TypeTextAsync(session.Id, nameTarget, safeValue, CancellationToken.None);

            await engine.PhysicalClickAsync(session.Id, passwordTarget, CancellationToken.None);
            await WaitUntilAsync(
                () => recorder.Current.Steps.Count >= 2,
                TimeSpan.FromSeconds(5),
                () => DescribeRecorder(recorder));
            await engine.TypeTextAsync(session.Id, passwordTarget, passwordValue, CancellationToken.None);
            await recorder.StopAsync(CancellationToken.None);

            var steps = recorder.Current.Steps;
            Assert.Equal(2, steps.Count);
            Assert.Equal(RecordedActionType.Fill, steps[0].Action);
            Assert.Equal(safeValue, steps[0].Input);
            Assert.Contains(
                steps[0].Target.Locators,
                locator => locator.Kind == LocatorKind.AutomationId && locator.Value == "txtName");
            Assert.Equal(RecordedActionType.Click, steps[1].Action);
            Assert.Null(steps[1].Input);
            Assert.Contains(
                steps[1].Target.Locators,
                locator => locator.Kind == LocatorKind.AutomationId && locator.Value == "txtPassword");
            Assert.DoesNotContain(steps, step => string.Equals(step.Input, passwordValue, StringComparison.Ordinal));

            var passwordInfo = await engine.GetValueAsync(session.Id, passwordTarget, CancellationToken.None);
            Assert.True(passwordInfo.IsPassword);
            Assert.Null(passwordInfo.Value);

            await engine.TypeTextAsync(session.Id, nameTarget, string.Empty, CancellationToken.None);
            await engine.TypeTextAsync(
                session.Id,
                steps[0].Target,
                steps[0].Input ?? throw new InvalidOperationException("O passo Fill não possui valor."),
                CancellationToken.None);
            var replayedValue = await engine.GetValueAsync(session.Id, nameTarget, CancellationToken.None);
            Assert.Equal(safeValue, replayedValue.Value);
        }
        finally
        {
            if (recorder.Current.IsRecording)
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await recorder.StopAsync(stopTimeout.Token);
            }

            await recorder.DisposeAsync();
            await engine.DisposeAsync();

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        Func<string>? timeoutMessage = null)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            while (!condition())
            {
                await Task.Delay(50, cancellation.Token);
            }
        }
        catch (OperationCanceledException exception) when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage?.Invoke() ?? "A condição esperada não foi atingida.", exception);
        }
    }

    private static string DescribeRecorder(IDesktopInputRecorder recorder) =>
        $"Recorder status: {recorder.Current.StatusMessage}; steps: " +
        string.Join(
            ", ",
            recorder.Current.Steps.Select(step =>
                $"{step.Action}|{step.DisplayName}|{step.ClassName}|{step.ControlType}"));

    private static async Task<AutomationElementInfo> FindElementEventuallyAsync(
        IAutomationEngine engine,
        Guid sessionId,
        LocatorSet target)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var element = await engine.FindElementAsync(sessionId, target, cancellation.Token);
            if (element is not null)
            {
                return element;
            }

            await Task.Delay(100, cancellation.Token);
        }
    }

    private static string ResolvePlaygroundPath()
    {
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var executable = Path.Combine(
            workspace,
            "src",
            "QASentinel.TestPlayground",
            "bin",
            configuration,
            "net10.0-windows",
            "QASentinel.TestPlayground.exe");
        Assert.True(File.Exists(executable), $"Playground não encontrado em {executable}");
        return executable;
    }
}
