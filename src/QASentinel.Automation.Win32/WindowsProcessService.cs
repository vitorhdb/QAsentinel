using System.Diagnostics;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;

namespace QASentinel.Automation.Win32;

public sealed class WindowsProcessService(ITopLevelWindowService windowService) : IProcessService
{
    public Task<IReadOnlyList<ProcessDescriptor>> GetProcessesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ProcessDescriptor>>(() =>
        {
            var processes = new List<ProcessDescriptor>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (process.Id <= 0 || process.Id == Environment.ProcessId)
                        {
                            continue;
                        }

                        var window = TopLevelWindowSelector.SelectBest(
                            windowService.GetWindows(process.Id),
                            process.MainWindowHandle);
                        if (window is null || !window.IsVisible || window.Bounds.IsEmpty)
                        {
                            continue;
                        }

                        processes.Add(new ProcessDescriptor(
                            process.Id,
                            process.ProcessName,
                            window.Title,
                            TryGetExecutablePath(process),
                            GetArchitecture(process),
                            window.Handle));
                    }
                    catch (InvalidOperationException)
                    {
                        // The process ended between enumeration and property access.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Protected processes are intentionally omitted.
                    }
                }
            }

            return processes
                .OrderBy(process => process.ProcessName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(process => process.WindowTitle, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);

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
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string GetArchitecture(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return "x86";
        }

        try
        {
            if (!NativeMethods.IsWow64Process2(process.Handle, out var processMachine, out var nativeMachine))
            {
                return "Desconhecida";
            }

            if (processMachine != 0)
            {
                return "x86";
            }

            return nativeMachine == 0xAA64 ? "ARM64" : "x64";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Desconhecida";
        }
        catch (InvalidOperationException)
        {
            return "Desconhecida";
        }
    }
}
