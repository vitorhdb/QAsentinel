using System.ComponentModel;
using System.Runtime.InteropServices;
using QASentinel.Recorder;

namespace QASentinel.Automation.Win32;

public sealed class WindowsMouseHookSource : IPointerInputSource
{
    private readonly object _sync = new();
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private Thread? _thread;
    private TaskCompletionSource? _ready;
    private TaskCompletionSource? _stopped;
    private nint _hook;
    private uint _threadId;
    private uint _targetProcessId;

    public WindowsMouseHookSource()
    {
        _callback = HookCallback;
    }

    public event Action<RawPointerEvent>? EventReceived;

    public async Task StartAsync(int processId, CancellationToken cancellationToken)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        Task readyTask;
        lock (_sync)
        {
            if (_thread is not null)
            {
                throw new InvalidOperationException("O hook de mouse já está ativo.");
            }

            _targetProcessId = checked((uint)processId);
            _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "QA Sentinel Mouse Recorder"
            };
            _thread.Start();
            readyTask = _ready.Task;
        }

        await readyTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? stoppedTask;
        uint threadId;
        lock (_sync)
        {
            if (_thread is null)
            {
                return;
            }

            stoppedTask = _stopped?.Task;
            threadId = _threadId;
        }

        if (threadId != 0)
        {
            _ = NativeMethods.PostThreadMessage(threadId, NativeMethods.WmQuit, 0, 0);
        }

        if (stoppedTask is not null)
        {
            await stoppedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _thread = null;
            _ready = null;
            _stopped = null;
            _threadId = 0;
            _targetProcessId = 0;
        }
    }

    private void RunMessageLoop()
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _ = NativeMethods.PeekMessage(
                out _,
                nint.Zero,
                0,
                0,
                NativeMethods.PmNoRemove);
            _hook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhMouseLowLevel,
                _callback,
                NativeMethods.GetModuleHandle(null),
                0);
            if (_hook == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível instalar o hook de mouse.");
            }

            _ready?.TrySetResult();
            while (NativeMethods.GetMessage(out var message, nint.Zero, 0, 0) > 0)
            {
                _ = message;
            }
        }
        catch (Exception exception)
        {
            _ready?.TrySetException(exception);
        }
        finally
        {
            if (_hook != nint.Zero)
            {
                _ = NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = nint.Zero;
            }

            _stopped?.TrySetResult();
        }
    }

    private nint HookCallback(int code, nuint message, nint parameter)
    {
        if (code >= 0 &&
            (message == NativeMethods.WmLeftButtonDown || message == NativeMethods.WmLeftButtonUp))
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.LowLevelMouseHookData>(parameter);
                var window = NativeMethods.WindowFromPoint(data.Point);
                if (window != nint.Zero)
                {
                    _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
                    if (processId == _targetProcessId)
                    {
                        EventReceived?.Invoke(new RawPointerEvent(
                            message == NativeMethods.WmLeftButtonDown ? PointerEventKind.Down : PointerEventKind.Up,
                            PointerButton.Left,
                            data.Point.X,
                            data.Point.Y,
                            DateTimeOffset.UtcNow));
                    }
                }
            }
            catch
            {
                // Hook callbacks must never propagate into the Windows message chain.
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, message, parameter);
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await StopAsync(timeout.Token).ConfigureAwait(false);
    }
}
