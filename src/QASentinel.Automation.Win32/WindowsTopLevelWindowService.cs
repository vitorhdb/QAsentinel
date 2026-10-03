using System.ComponentModel;
using System.Runtime.InteropServices;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;

namespace QASentinel.Automation.Win32;

public sealed class WindowsTopLevelWindowService : ITopLevelWindowService
{
    public IReadOnlyList<TopLevelWindowDescriptor> GetWindows(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var windows = new List<TopLevelWindowDescriptor>();
        var enumerated = NativeMethods.EnumWindows((windowHandle, ignoredParameter) =>
        {
            _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var ownerProcessId);
            if (ownerProcessId != (uint)processId)
            {
                return true;
            }

            var bounds = NativeMethods.GetWindowRect(windowHandle, out var rect)
                ? new BoundingRectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
                : default;
            var extendedStyle = NativeMethods.GetWindowLongPtrW(windowHandle, NativeMethods.GwlExStyle).ToInt64();
            windows.Add(new TopLevelWindowDescriptor(
                windowHandle,
                ReadWindowText(windowHandle),
                ReadClassName(windowHandle),
                NativeMethods.IsWindowVisible(windowHandle),
                NativeMethods.IsWindowEnabled(windowHandle),
                (extendedStyle & NativeMethods.WsExToolWindow) != 0,
                NativeMethods.GetWindow(windowHandle, NativeMethods.GwOwner),
                bounds));
            return true;
        }, nint.Zero);

        if (!enumerated)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return windows;
    }

    private static string ReadWindowText(nint windowHandle)
    {
        var length = NativeMethods.GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowText(windowHandle, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }

    private static string ReadClassName(nint windowHandle)
    {
        var buffer = new char[256];
        var copied = NativeMethods.GetClassName(windowHandle, buffer, buffer.Length);
        return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
    }
}
