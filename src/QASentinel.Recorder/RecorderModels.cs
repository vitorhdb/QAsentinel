using QASentinel.Automation.Models;

namespace QASentinel.Recorder;

public enum PointerEventKind
{
    Down,
    Up
}

public enum PointerButton
{
    Left
}

public enum RecordedActionType
{
    Click,
    Fill
}

public sealed record RawPointerEvent(
    PointerEventKind Kind,
    PointerButton Button,
    double X,
    double Y,
    DateTimeOffset Timestamp);

public sealed record NormalizedRecordedAction(
    RecordedActionType Action,
    AutomationElementInfo Element,
    string? Input,
    DateTimeOffset CapturedAt);

public sealed record RecordedStep(
    Guid Id,
    int Sequence,
    RecordedActionType Action,
    string DisplayName,
    string? ClassName,
    string? ControlType,
    LocatorSet Target,
    string? Input,
    DateTimeOffset CapturedAt);

public sealed record RecorderSnapshot(
    bool IsRecording,
    Guid? SessionId,
    IReadOnlyList<RecordedStep> Steps,
    string StatusMessage);
