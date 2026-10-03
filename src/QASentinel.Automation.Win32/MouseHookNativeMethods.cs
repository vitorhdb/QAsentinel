using System.Runtime.InteropServices;

namespace QASentinel.Automation.Win32;

internal static partial class NativeMethods
{
    internal const int WhMouseLowLevel = 14;
    internal const uint WmLeftButtonDown = 0x0201;
    internal const uint WmLeftButtonUp = 0x0202;
    internal const uint WmQuit = 0x0012;
    internal const uint PmNoRemove = 0x0000;

    internal delegate nint LowLevelMouseProc(int code, nuint message, nint parameter);

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    internal static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelMouseProc callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nuint message, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessage(out Message message, nint windowHandle, uint minimum, uint maximum);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        out Message message,
        nint windowHandle,
        uint minimum,
        uint maximum,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint threadId, uint message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(Point point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelMouseHookData
    {
        internal Point Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint WindowHandle;
        internal uint Value;
        internal nuint WordParameter;
        internal nint LongParameter;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }
}
