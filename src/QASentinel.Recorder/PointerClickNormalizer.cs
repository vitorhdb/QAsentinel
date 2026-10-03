using QASentinel.Automation.Models;

namespace QASentinel.Recorder;

public sealed class PointerClickNormalizer(
    TimeSpan? maximumClickDuration = null,
    double maximumMovement = 8)
{
    private readonly TimeSpan _maximumClickDuration = maximumClickDuration ?? TimeSpan.FromSeconds(2);
    private PendingPointerDown? _pending;

    public NormalizedRecordedAction? Process(RawPointerEvent input, AutomationElementInfo? element)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind == PointerEventKind.Down)
        {
            _pending = element is null ? null : new PendingPointerDown(input, element);
            return null;
        }

        var pending = _pending;
        _pending = null;
        if (pending is null ||
            input.Button != pending.Input.Button ||
            input.Timestamp < pending.Input.Timestamp ||
            input.Timestamp - pending.Input.Timestamp > _maximumClickDuration)
        {
            return null;
        }

        var deltaX = input.X - pending.Input.X;
        var deltaY = input.Y - pending.Input.Y;
        if (Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) > maximumMovement)
        {
            return null;
        }

        return new NormalizedRecordedAction(
            RecordedActionType.Click,
            pending.Element,
            null,
            input.Timestamp);
    }

    public void Reset() => _pending = null;

    private sealed record PendingPointerDown(RawPointerEvent Input, AutomationElementInfo Element);
}
