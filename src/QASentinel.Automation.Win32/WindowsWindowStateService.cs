using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;

namespace QASentinel.Automation.Win32;

public sealed class WindowsWindowStateService : IWindowStateService
{
    public WindowPresentationState GetState(nint windowHandle)
    {
        ValidateHandle(windowHandle);
        if (NativeMethods.IsIconic(windowHandle))
        {
            return WindowPresentationState.Minimized;
        }

        return NativeMethods.IsZoomed(windowHandle)
            ? WindowPresentationState.Maximized
            : WindowPresentationState.Normal;
    }

    public void Restore(nint windowHandle)
    {
        ValidateHandle(windowHandle);
        _ = NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SwRestore);
    }

    public void Minimize(nint windowHandle)
    {
        ValidateHandle(windowHandle);
        _ = NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SwMinimize);
    }

    public bool Activate(nint windowHandle)
    {
        ValidateHandle(windowHandle);
        return NativeMethods.SetForegroundWindow(windowHandle);
    }

    private static void ValidateHandle(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("O handle da janela não pode ser zero.", nameof(windowHandle));
        }
    }
}
