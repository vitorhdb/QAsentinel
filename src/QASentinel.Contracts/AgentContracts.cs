using System.Text.Json;
using System.Text.Json.Serialization;

namespace QASentinel.Contracts;

public static class AgentProtocol
{
    public const int Version = 1;
    public const string PipeName = "qa-sentinel-agent-v1";
    public const int MaximumMessageCharacters = 1_048_576;
}

public static class AgentOperations
{
    public const string Handshake = "system.handshake";
    public const string GetProcesses = "process.list";
    public const string Attach = "application.attach";
    public const string StartInspector = "inspector.start";
    public const string StopInspector = "inspector.stop";
    public const string GetCurrentElement = "inspector.current";
    public const string Click = "element.click";
    public const string Focus = "element.focus";
    public const string Input = "element.input";
    public const string Screenshot = "evidence.screenshot";
    public const string StartExecution = "execution.start";
    public const string GetExecution = "execution.status";
    public const string CancelExecution = "execution.cancel";
    public const string StartRecording = "recorder.start";
    public const string GetRecording = "recorder.status";
    public const string StopRecording = "recorder.stop";
}

public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}

public sealed record IpcRequestEnvelope(int ProtocolVersion, Guid RequestId, string Operation, JsonElement Payload);

public sealed record IpcResponseEnvelope(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    JsonElement? Payload,
    AgentErrorDto? Error);

public sealed record AgentErrorDto(string Code, string Message, string? Detail = null);

public sealed record HandshakeRequest(string ClientName, string ClientVersion);

public sealed record HandshakeResponse(string AgentVersion, int ProtocolVersion, string MachineName, string AutomationBackend);

public sealed record GetProcessesRequest(bool OnlyWithVisibleWindow = true);

public sealed record ProcessInfoDto(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    string? ExecutablePath,
    string Architecture,
    long MainWindowHandle);

public sealed record GetProcessesResponse(IReadOnlyList<ProcessInfoDto> Processes);

public sealed record AttachApplicationRequest(int ProcessId, int TimeoutMilliseconds = 5000);

public sealed record AttachApplicationResponse(ApplicationSessionDto Session);

public sealed record ApplicationSessionDto(
    Guid SessionId,
    int ProcessId,
    string? ExecutablePath,
    string ProcessName,
    long MainWindowHandle,
    string WindowTitle,
    DateTimeOffset StartedAt,
    string AutomationBackend);

public enum LocatorKindDto
{
    AutomationId,
    Name,
    ClassName,
    ControlType,
    NativeWindowHandle,
    Parent,
    Index,
    Coordinates
}

public sealed record ElementLocatorDto(LocatorKindDto Kind, string Value, int Score);

public sealed record LocatorSetDto(IReadOnlyList<ElementLocatorDto> Locators);

public sealed record BoundingRectangleDto(double X, double Y, double Width, double Height);

public sealed record AutomationElementInfoDto(
    string? Name,
    string? AutomationId,
    string? ClassName,
    string? ControlType,
    string? FrameworkId,
    string? HelpText,
    long NativeWindowHandle,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsFocused,
    BoundingRectangleDto Bounds,
    LocatorSetDto Locators,
    IReadOnlyList<string> SupportedPatterns);

public sealed record StartInspectorRequest(Guid SessionId, int PollIntervalMilliseconds = 120);

public sealed record StopInspectorRequest(Guid SessionId);

public sealed record InspectorStateResponse(bool IsRunning, AutomationElementInfoDto? Element, string? StatusMessage);

public sealed record ElementActionRequest(Guid SessionId, LocatorSetDto Target);

public sealed record InputElementRequest(Guid SessionId, LocatorSetDto Target, string Text);

public enum ScreenshotTargetDto
{
    FullScreen,
    Application,
    Element
}

public sealed record CaptureScreenshotRequest(
    Guid SessionId,
    ScreenshotTargetDto Target,
    LocatorSetDto? Element,
    string? OutputDirectory = null);

public sealed record CaptureScreenshotResponse(string FilePath, int Width, int Height, DateTimeOffset CapturedAt);

public sealed record OperationResponse(bool Succeeded, string Message);

public enum TestActionDto
{
    Click,
    Focus,
    Input,
    WaitForElement,
    AssertExists,
    AssertVisible,
    AssertEnabled
}

public enum TestExecutionStatusDto
{
    Running,
    Passed,
    Failed,
    Error,
    Cancelled
}

public enum StepExecutionStatusDto
{
    Running,
    Passed,
    Failed,
    Error,
    Cancelled,
    Skipped
}

public sealed record ExecutableTestStepDto(
    Guid StepId,
    int Sequence,
    TestActionDto Action,
    LocatorSetDto Target,
    string? Input,
    int TimeoutMilliseconds,
    int MaxAttempts);

public sealed record StartTestExecutionRequest(
    Guid ExecutionId,
    Guid SessionId,
    Guid TestCaseId,
    string TestCaseName,
    IReadOnlyList<ExecutableTestStepDto> Steps);

public sealed record GetTestExecutionRequest(Guid ExecutionId);

public sealed record CancelTestExecutionRequest(Guid ExecutionId);

public sealed record StepExecutionResultDto(
    Guid StepId,
    int Sequence,
    TestActionDto Action,
    StepExecutionStatusDto Status,
    int Attempts,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long DurationMilliseconds,
    string? ErrorCode,
    string? Message);

public sealed record TestExecutionStateResponse(
    Guid ExecutionId,
    Guid TestCaseId,
    Guid SessionId,
    TestExecutionStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long DurationMilliseconds,
    string MachineName,
    string? ApplicationVersion,
    IReadOnlyList<StepExecutionResultDto> Steps,
    string? ErrorMessage);

public enum RecordedActionDto
{
    Click,
    Fill
}

public sealed record StartRecordingRequest(Guid SessionId);

public sealed record StopRecordingRequest(Guid SessionId);

public sealed record GetRecordingStateRequest;

public sealed record RecordedStepDto(
    Guid StepId,
    int Sequence,
    RecordedActionDto Action,
    string DisplayName,
    string? ClassName,
    string? ControlType,
    LocatorSetDto Target,
    string? Input,
    DateTimeOffset CapturedAt);

public sealed record RecorderStateResponse(
    bool IsRecording,
    Guid? SessionId,
    IReadOnlyList<RecordedStepDto> Steps,
    string StatusMessage);
