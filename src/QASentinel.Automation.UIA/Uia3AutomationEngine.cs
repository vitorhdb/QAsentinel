using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Exceptions;
using QASentinel.Automation.Models;
using FlaUIApplication = FlaUI.Core.Application;

namespace QASentinel.Automation.UIA;

public sealed class Uia3AutomationEngine(
    ITopLevelWindowService windowService,
    IWindowStateService windowStateService,
    ILogger<Uia3AutomationEngine> logger) : IAutomationEngine, IElementResolver
{
    private readonly ConcurrentDictionary<Guid, SessionContext> _sessions = new();
    private readonly SemaphoreSlim _automationGate = new(1, 1);

    public async Task<ApplicationSession> AttachAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            var executablePath = TryGetExecutablePath(process);
            var application = FlaUIApplication.Attach(processId);
            var automation = new UIA3Automation();

            Window? mainWindow;
            try
            {
                mainWindow = await Task.Run(() =>
                    {
                        var selectedWindow = TopLevelWindowSelector.SelectBest(
                            windowService.GetWindows(processId),
                            process.MainWindowHandle);
                        if (selectedWindow is not null)
                        {
                            return automation.FromHandle(selectedWindow.Handle)?.AsWindow();
                        }

                        return application.GetMainWindow(automation, timeout);
                    }, cancellationToken)
                    .WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                automation.Dispose();
                application.Dispose();
                throw;
            }

            if (mainWindow is null)
            {
                automation.Dispose();
                application.Dispose();
                throw new WindowNotFoundException(processId);
            }

            var handle = new nint(mainWindow.Properties.NativeWindowHandle.ValueOrDefault);
            var session = new ApplicationSession(
                Guid.NewGuid(),
                processId,
                executablePath,
                processName,
                handle,
                SafeRead(() => mainWindow.Name),
                DateTimeOffset.Now,
                AutomationBackend.Uia3);

            if (!_sessions.TryAdd(session.Id, new SessionContext(session, application, automation, mainWindow)))
            {
                automation.Dispose();
                application.Dispose();
                throw new ApplicationAttachException(processId);
            }

            logger.LogInformation(
                "Operation=AttachApplication ProcessId={ProcessId} Backend=UIA3 DurationMs={DurationMs} Result=Success",
                processId,
                stopwatch.ElapsedMilliseconds);
            return session;
        }
        catch (ArgumentException)
        {
            throw new ApplicationNotFoundException(processId);
        }
        catch (TimeoutException exception)
        {
            throw new AutomationTimeoutException("AttachApplication", timeout, exception);
        }
        catch (AutomationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Operation=AttachApplication ProcessId={ProcessId} Backend=UIA3 DurationMs={DurationMs} Result=Failure",
                processId,
                stopwatch.ElapsedMilliseconds);
            throw new ApplicationAttachException(processId, exception);
        }
    }

    public async Task<AutomationElementInfo?> InspectAtAsync(
        Guid sessionId,
        double x,
        double y,
        CancellationToken cancellationToken) =>
        await InspectAtCoreAsync(sessionId, x, y, actionable: false, cancellationToken).ConfigureAwait(false);

    public async Task<AutomationElementInfo?> InspectActionableAtAsync(
        Guid sessionId,
        double x,
        double y,
        CancellationToken cancellationToken) =>
        await InspectAtCoreAsync(sessionId, x, y, actionable: true, cancellationToken).ConfigureAwait(false);

    private async Task<AutomationElementInfo?> InspectAtCoreAsync(
        Guid sessionId,
        double x,
        double y,
        bool actionable,
        CancellationToken cancellationToken)
    {
        var context = GetContext(sessionId);
        await _automationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var element = context.Automation.FromPoint(new Point(
                    checked((int)Math.Round(x)),
                    checked((int)Math.Round(y))));
                if (element is null || GetProcessId(element) != context.Session.ProcessId)
                {
                    return null;
                }

                if (actionable)
                {
                    element = PromoteToActionableAncestor(element, context.Session.ProcessId);
                }

                return MapElement(element);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AutomationBackendException("UIA3", exception);
        }
        finally
        {
            _automationGate.Release();
        }
    }

    public async Task<AutomationElementInfo?> FindElementAsync(
        Guid sessionId,
        LocatorSet locators,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locators);
        var context = GetContext(sessionId);
        await _automationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var element = FindElement(context, locators);
                return element is null ? null : MapElement(element);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _automationGate.Release();
        }
    }

    public Task<AutomationElementInfo?> ResolveAsync(
        Guid sessionId,
        LocatorSet locators,
        CancellationToken cancellationToken) => FindElementAsync(sessionId, locators, cancellationToken);

    public async Task<AutomationElementInfo?> GetFocusedElementAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var context = GetContext(sessionId);
        await _automationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var element = context.Automation.FocusedElement();
                if (element is null || GetProcessId(element) != context.Session.ProcessId)
                {
                    return null;
                }

                return MapElement(PromoteToActionableAncestor(element, context.Session.ProcessId));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AutomationBackendException("UIA3", exception);
        }
        finally
        {
            _automationGate.Release();
        }
    }

    public async Task<AutomationValueInfo> GetValueAsync(
        Guid sessionId,
        LocatorSet locators,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locators);
        var context = GetContext(sessionId);
        await _automationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var element = FindElement(context, locators) ?? throw new ElementNotFoundException();
                var isPassword = SafeRead(() => element.Properties.IsPassword.ValueOrDefault);
                if (!element.Patterns.Value.TryGetPattern(out var valuePattern))
                {
                    return new AutomationValueInfo(false, false, isPassword, null);
                }

                var isReadOnly = SafeRead(() => valuePattern.IsReadOnly.ValueOrDefault);
                var value = isPassword
                    ? null
                    : SafeRead(() => valuePattern.Value.ValueOrDefault);
                return new AutomationValueInfo(true, isReadOnly, isPassword, value);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _automationGate.Release();
        }
    }

    public async Task<AutomationCompatibilityScan> ScanCompatibilityAsync(
        Guid sessionId,
        CompatibilityScanOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var stopwatch = Stopwatch.StartNew();
        var context = GetContext(sessionId);
        await _automationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await Task.Run(
                () => ScanCompatibility(context, options, cancellationToken),
                CancellationToken.None).ConfigureAwait(false);

            logger.LogInformation(
                "Operation=ScanCompatibility ProcessId={ProcessId} Backend=UIA3 ElementCount={ElementCount} " +
                "LimitReached={LimitReached} DurationMs={DurationMs} Result=Success",
                context.Session.ProcessId,
                result.Elements.Count,
                result.ElementLimitReached,
                stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AutomationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Operation=ScanCompatibility ProcessId={ProcessId} Backend=UIA3 DurationMs={DurationMs} Result=Failure",
                context.Session.ProcessId,
                stopwatch.ElapsedMilliseconds);
            throw new AutomationBackendException("UIA3", exception);
        }
        finally
        {
            _automationGate.Release();
        }
    }

    public Task ClickAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken) =>
        ExecuteOnElementAsync(sessionId, locators, "Click", (context, element) =>
        {
            if (element.Patterns.Invoke.TryGetPattern(out var invokePattern))
            {
                invokePattern.Invoke();
            }
            else if (element.Patterns.SelectionItem.TryGetPattern(out var selectionItemPattern))
            {
                selectionItemPattern.Select();
                if (WaitForPhysicalInputSafety(context, element))
                {
                    element.Click();
                }
                else if (element.Patterns.LegacyIAccessible.TryGetPattern(out var legacyPattern) &&
                         !string.IsNullOrWhiteSpace(legacyPattern.DefaultAction.ValueOrDefault))
                {
                    legacyPattern.DoDefaultAction();
                }
                else
                {
                    throw new ElementNotInteractableException("Click");
                }
            }
            else if (element.Patterns.LegacyIAccessible.TryGetPattern(out var legacyPattern) &&
                     !string.IsNullOrWhiteSpace(legacyPattern.DefaultAction.ValueOrDefault))
            {
                legacyPattern.DoDefaultAction();
            }
            else
            {
                EnsurePhysicalInputIsSafe(context, element, "Click");
                element.Click();
            }
        }, cancellationToken);

    public Task PhysicalClickAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken) =>
        ExecuteOnElementAsync(sessionId, locators, "PhysicalClick", (context, element) =>
        {
            EnsurePhysicalInputIsSafe(context, element, "PhysicalClick");
            _ = windowStateService.Activate(context.Session.MainWindowHandle);
            element.Focus();
            Thread.Sleep(150);
            var bounds = element.BoundingRectangle;
            Mouse.LeftClick(new Point(
                checked(bounds.X + (bounds.Width / 2)),
                checked(bounds.Y + (bounds.Height / 2))));
        }, cancellationToken);

    public Task FocusAsync(Guid sessionId, LocatorSet locators, CancellationToken cancellationToken) =>
        ExecuteOnElementAsync(sessionId, locators, "Focus", (_, element) => element.Focus(), cancellationToken);

    public Task TypeTextAsync(
        Guid sessionId,
        LocatorSet locators,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ExecuteOnElementAsync(sessionId, locators, "Input", (context, element) =>
        {
            if (element.Patterns.Value.TryGetPattern(out var valuePattern) && !valuePattern.IsReadOnly.ValueOrDefault)
            {
                valuePattern.SetValue(text);
            }
            else
            {
                EnsurePhysicalInputIsSafe(context, element, "Input");
                element.Focus();
                Keyboard.Type(text);
            }
        }, cancellationToken);
    }

    public ApplicationSession GetSession(Guid sessionId) => GetContext(sessionId).Session;

    private async Task ExecuteOnElementAsync(
        Guid sessionId,
        LocatorSet locators,
        string operation,
        Action<SessionContext, AutomationElement> action,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = GetContext(sessionId);
        await _automationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var element = FindElement(context, locators) ?? throw new ElementNotFoundException();
                if (windowStateService.GetState(context.Session.MainWindowHandle) == WindowPresentationState.Minimized ||
                    !SafeRead(() => element.IsEnabled))
                {
                    throw new ElementNotInteractableException(operation);
                }

                action(context, element);
            }, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Operation={Operation} ProcessId={ProcessId} Backend=UIA3 DurationMs={DurationMs} Result=Success",
                operation,
                context.Session.ProcessId,
                stopwatch.ElapsedMilliseconds);
        }
        catch (AutomationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Operation={Operation} ProcessId={ProcessId} Backend=UIA3 DurationMs={DurationMs} Result=Failure",
                operation,
                context.Session.ProcessId,
                stopwatch.ElapsedMilliseconds);
            throw new ElementNotInteractableException(operation, exception);
        }
        finally
        {
            _automationGate.Release();
        }
    }

    private static AutomationElement? FindElement(SessionContext context, LocatorSet locators)
    {
        var handleLocator = locators.Locators.FirstOrDefault(locator => locator.Kind == LocatorKind.NativeWindowHandle);
        if (handleLocator is not null && long.TryParse(handleLocator.Value, out var handle) && handle != 0)
        {
            try
            {
                var byHandle = context.Automation.FromHandle(new nint(handle));
                if (byHandle is not null && GetProcessId(byHandle) == context.Session.ProcessId && Matches(byHandle, locators))
                {
                    return byHandle;
                }
            }
            catch
            {
                // HWNDs are ephemeral; continue with semantic locators.
            }
        }

        var automationId = GetValue(locators, LocatorKind.AutomationId);
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            var byAutomationId = context.MainWindow.FindFirstDescendant(factory => factory.ByAutomationId(automationId));
            if (byAutomationId is not null && Matches(byAutomationId, locators))
            {
                return byAutomationId;
            }
        }

        var name = GetValue(locators, LocatorKind.Name);
        var className = GetValue(locators, LocatorKind.ClassName);
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(className))
        {
            var byNameAndClass = context.MainWindow.FindFirstDescendant(factory =>
                factory.ByName(name).And(factory.ByClassName(className)));
            if (byNameAndClass is not null)
            {
                return byNameAndClass;
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = context.MainWindow.FindFirstDescendant(factory => factory.ByName(name));
            if (byName is not null && Matches(byName, locators, allowMissingClass: true))
            {
                return byName;
            }
        }

        if (!string.IsNullOrWhiteSpace(className))
        {
            return context.MainWindow.FindFirstDescendant(factory => factory.ByClassName(className));
        }

        return null;
    }

    private static bool Matches(AutomationElement element, LocatorSet locators, bool allowMissingClass = false)
    {
        var automationId = GetValue(locators, LocatorKind.AutomationId);
        if (!string.IsNullOrWhiteSpace(automationId) &&
            !string.Equals(SafeRead(() => element.AutomationId), automationId, StringComparison.Ordinal))
        {
            return false;
        }

        var name = GetValue(locators, LocatorKind.Name);
        if (!string.IsNullOrWhiteSpace(name) &&
            !string.Equals(SafeRead(() => element.Name), name, StringComparison.Ordinal))
        {
            return false;
        }

        var className = GetValue(locators, LocatorKind.ClassName);
        if (!allowMissingClass && !string.IsNullOrWhiteSpace(className) &&
            !string.Equals(SafeRead(() => element.ClassName), className, StringComparison.Ordinal))
        {
            return false;
        }

        var controlType = GetValue(locators, LocatorKind.ControlType);
        if (!string.IsNullOrWhiteSpace(controlType) &&
            !string.Equals(SafeRead(() => element.ControlType.ToString()), controlType, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static void EnsurePhysicalInputIsSafe(
        SessionContext context,
        AutomationElement element,
        string operation)
    {
        if (!IsPhysicalInputSafe(context, element))
        {
            throw new ElementNotInteractableException(operation);
        }
    }

    private static bool WaitForPhysicalInputSafety(SessionContext context, AutomationElement element)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (IsPhysicalInputSafe(context, element))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static bool IsPhysicalInputSafe(SessionContext context, AutomationElement element)
    {
        if (!SafeRead(() => element.IsOffscreen, true))
        {
            return true;
        }

        var elementBounds = SafeRead(() => element.BoundingRectangle, Rectangle.Empty);
        var windowBounds = SafeRead(() => context.MainWindow.BoundingRectangle, Rectangle.Empty);
        return !elementBounds.IsEmpty && !windowBounds.IsEmpty && elementBounds.IntersectsWith(windowBounds);
    }

    private static string? GetValue(LocatorSet locators, LocatorKind kind) =>
        locators.Locators.FirstOrDefault(locator => locator.Kind == kind)?.Value;

    private static AutomationElement PromoteToActionableAncestor(AutomationElement element, int processId)
    {
        var original = element;
        var current = element;
        for (var depth = 0; depth < 12; depth++)
        {
            if (IsActionable(current))
            {
                return current;
            }

            var parent = SafeRead(() => current.Parent);
            if (parent is null || GetProcessId(parent) != processId)
            {
                break;
            }

            current = parent;
        }

        return original;
    }

    private static bool IsActionable(AutomationElement element) => SafeRead(() =>
        string.Equals(element.ControlType.ToString(), "Edit", StringComparison.Ordinal) ||
        element.Patterns.Invoke.TryGetPattern(out _) ||
        element.Patterns.SelectionItem.TryGetPattern(out _) ||
        element.Patterns.Toggle.TryGetPattern(out _) ||
        element.Patterns.ExpandCollapse.TryGetPattern(out _) ||
        (element.Patterns.LegacyIAccessible.TryGetPattern(out var legacyPattern) &&
         !string.IsNullOrWhiteSpace(legacyPattern.DefaultAction.ValueOrDefault)),
        false);

    private static AutomationElementInfo MapElement(AutomationElement element)
    {
        var bounds = SafeRead(() => element.BoundingRectangle, Rectangle.Empty);
        var name = SafeRead(() => element.Name);
        var automationId = SafeRead(() => element.AutomationId);
        var className = SafeRead(() => element.ClassName);
        var controlType = SafeRead(() => element.ControlType.ToString());
        var frameworkId = SafeRead(() => element.Properties.FrameworkId.ValueOrDefault);
        var helpText = SafeRead(() => element.HelpText);
        var handle = SafeRead(() => element.Properties.NativeWindowHandle.ValueOrDefault);
        var parent = SafeRead(() => element.Parent);
        var parentIdentity = parent is null
            ? null
            : $"{SafeRead(() => parent.AutomationId)}|{SafeRead(() => parent.Name)}|{SafeRead(() => parent.ClassName)}";

        var locatorValues = new List<ElementLocator>();
        if (!IsNativeHandleAutomationId(automationId, handle))
        {
            locatorValues.Add(new ElementLocator(LocatorKind.AutomationId, automationId));
        }

        if (handle != nint.Zero)
        {
            locatorValues.Add(new ElementLocator(LocatorKind.NativeWindowHandle, handle.ToString()));
        }

        locatorValues.AddRange(
        [
            new(LocatorKind.Name, name),
            new(LocatorKind.ClassName, className),
            new(LocatorKind.ControlType, controlType),
            new(LocatorKind.Parent, parentIdentity ?? string.Empty),
            new(LocatorKind.Coordinates, $"{bounds.X:0.##},{bounds.Y:0.##}")
        ]);

        var patterns = SafeRead(
            () => (IReadOnlyList<string>)element.GetSupportedPatterns().Select(pattern => pattern.Name).OrderBy(name => name).ToArray(),
            Array.Empty<string>());

        return new AutomationElementInfo(
            name,
            automationId,
            className,
            controlType,
            frameworkId,
            helpText,
            new nint(handle),
            SafeRead(() => element.IsEnabled),
            SafeRead(() => element.IsOffscreen, true),
            SafeRead(() => element.Properties.HasKeyboardFocus.ValueOrDefault),
            new BoundingRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new LocatorSet(locatorValues),
            patterns);
    }

    private static bool IsNativeHandleAutomationId(string? automationId, nint nativeWindowHandle) =>
        nativeWindowHandle != nint.Zero &&
        long.TryParse(automationId, out var parsedAutomationId) &&
        parsedAutomationId == nativeWindowHandle.ToInt64();

    private static AutomationCompatibilityScan ScanCompatibility(
        SessionContext context,
        CompatibilityScanOptions options,
        CancellationToken cancellationToken)
    {
        var elements = new List<AutomationTreeElementInfo>(Math.Min(options.MaxElements, 512));
        var warnings = new List<string>();
        var pending = new Queue<PendingElement>();
        pending.Enqueue(new PendingElement(context.MainWindow, 0, null, 0));

        while (pending.Count > 0 && elements.Count < options.MaxElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            var sequence = elements.Count;
            elements.Add(new AutomationTreeElementInfo(
                sequence,
                current.Depth,
                current.ParentSequence,
                current.SiblingIndex,
                MapElement(current.Element)));

            if (current.Depth >= options.MaxDepth)
            {
                continue;
            }

            AutomationElement[] children;
            try
            {
                children = current.Element.FindAllChildren();
            }
            catch (Exception exception)
            {
                warnings.Add($"Falha ao enumerar filhos do elemento {sequence}: {exception.GetType().Name}.");
                continue;
            }

            for (var siblingIndex = 0; siblingIndex < children.Length; siblingIndex++)
            {
                pending.Enqueue(new PendingElement(children[siblingIndex], current.Depth + 1, sequence, siblingIndex));
            }
        }

        return new AutomationCompatibilityScan(
            context.Session,
            DateTimeOffset.Now,
            elements,
            pending.Count > 0,
            warnings);
    }

    private static int GetProcessId(AutomationElement element) =>
        SafeRead(() => element.Properties.ProcessId.ValueOrDefault);

    private SessionContext GetContext(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var context)
            ? context
            : throw new SessionNotFoundException(sessionId);

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string SafeRead(Func<string?> reader) => SafeRead(reader, string.Empty) ?? string.Empty;

    private static T SafeRead<T>(Func<T> reader, T fallback = default!)
    {
        try
        {
            return reader();
        }
        catch
        {
            return fallback;
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var context in _sessions.Values)
        {
            context.Automation.Dispose();
            context.Application.Dispose();
        }

        _sessions.Clear();
        _automationGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record SessionContext(
        ApplicationSession Session,
        FlaUIApplication Application,
        UIA3Automation Automation,
        Window MainWindow);

    private sealed record PendingElement(
        AutomationElement Element,
        int Depth,
        int? ParentSequence,
        int SiblingIndex);
}
