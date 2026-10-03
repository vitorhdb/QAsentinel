namespace QASentinel.Recorder;

public interface IPointerInputSource : IAsyncDisposable
{
    event Action<RawPointerEvent>? EventReceived;

    Task StartAsync(int processId, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IDesktopInputRecorder : IAsyncDisposable
{
    RecorderSnapshot Current { get; }

    Task StartAsync(Guid sessionId, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
