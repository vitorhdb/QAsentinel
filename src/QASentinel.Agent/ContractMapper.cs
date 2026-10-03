using QASentinel.Automation.Models;
using QASentinel.Contracts;
using QASentinel.Recorder;

namespace QASentinel.Agent;

public static class ContractMapper
{
    public static ProcessInfoDto ToDto(this ProcessDescriptor process) => new(
        process.ProcessId,
        process.ProcessName,
        process.WindowTitle,
        process.ExecutablePath,
        process.Architecture,
        process.MainWindowHandle.ToInt64());

    public static ApplicationSessionDto ToDto(this ApplicationSession session) => new(
        session.Id,
        session.ProcessId,
        session.ExecutablePath,
        session.ProcessName,
        session.MainWindowHandle.ToInt64(),
        session.WindowTitle,
        session.StartedAt,
        session.Backend.ToString().ToUpperInvariant());

    public static AutomationElementInfoDto ToDto(this AutomationElementInfo element) => new(
        element.Name,
        element.AutomationId,
        element.ClassName,
        element.ControlType,
        element.FrameworkId,
        element.HelpText,
        element.NativeWindowHandle.ToInt64(),
        element.IsEnabled,
        element.IsOffscreen,
        element.IsFocused,
        new BoundingRectangleDto(element.Bounds.X, element.Bounds.Y, element.Bounds.Width, element.Bounds.Height),
        new LocatorSetDto(element.Locators.Locators.Select(locator => new ElementLocatorDto(
            Enum.Parse<LocatorKindDto>(locator.Kind.ToString()), locator.Value, locator.Score)).ToArray()),
        element.SupportedPatterns);

    public static LocatorSet ToModel(this LocatorSetDto set) => new(
        set.Locators.Select(locator => new ElementLocator(
            Enum.Parse<LocatorKind>(locator.Kind.ToString()), locator.Value, locator.Score)));

    public static ScreenshotTarget ToModel(this ScreenshotTargetDto target) =>
        Enum.Parse<ScreenshotTarget>(target.ToString());

    public static LocatorSetDto ToDto(this LocatorSet set) => new(
        set.Locators.Select(locator => new ElementLocatorDto(
            Enum.Parse<LocatorKindDto>(locator.Kind.ToString()),
            locator.Value,
            locator.Score)).ToArray());

    public static RecorderStateResponse ToDto(this RecorderSnapshot snapshot) => new(
        snapshot.IsRecording,
        snapshot.SessionId,
        snapshot.Steps.Select(step => new RecordedStepDto(
            step.Id,
            step.Sequence,
            Enum.Parse<RecordedActionDto>(step.Action.ToString()),
            step.DisplayName,
            step.ClassName,
            step.ControlType,
            step.Target.ToDto(),
            step.Input,
            step.CapturedAt)).ToArray(),
        snapshot.StatusMessage);
}
