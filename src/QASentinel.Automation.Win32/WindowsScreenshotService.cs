using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Exceptions;
using QASentinel.Automation.Models;

namespace QASentinel.Automation.Win32;

public sealed class WindowsScreenshotService : IScreenshotService
{
    public Task<ScreenshotResult> CaptureAsync(
        ScreenshotTarget target,
        ApplicationSession session,
        AutomationElementInfo? element,
        string outputDirectory,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rectangle = ResolveCaptureRectangle(target, session, element);
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            throw new AutomationBackendException("Win32 Screenshot");
        }

        Directory.CreateDirectory(outputDirectory);
        var component = SanitizeFileName(element?.AutomationId ?? element?.Name ?? target.ToString());
        var timestamp = DateTime.Now;
        var filePath = GetAvailableFilePath(outputDirectory, $"{timestamp:yyyyMMdd_HHmmss}_{component}");

        using var bitmap = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rectangle.X, rectangle.Y, 0, 0, rectangle.Size, CopyPixelOperation.SourceCopy);
        }

        bitmap.Save(filePath, ImageFormat.Png);
        return new ScreenshotResult(filePath, rectangle.Width, rectangle.Height, DateTimeOffset.Now);
    }, cancellationToken);

    private static Rectangle ResolveCaptureRectangle(
        ScreenshotTarget target,
        ApplicationSession session,
        AutomationElementInfo? element) => target switch
        {
            ScreenshotTarget.FullScreen => new Rectangle(
                NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
                NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
                NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
                NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen)),
            ScreenshotTarget.Application => GetWindowRectangle(session.MainWindowHandle),
            ScreenshotTarget.Element when element is not null => new Rectangle(
                checked((int)Math.Round(element.Bounds.X)),
                checked((int)Math.Round(element.Bounds.Y)),
                Math.Max(1, checked((int)Math.Round(element.Bounds.Width))),
                Math.Max(1, checked((int)Math.Round(element.Bounds.Height)))),
            ScreenshotTarget.Element => throw new ElementNotFoundException(),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

    private static Rectangle GetWindowRectangle(nint windowHandle)
    {
        if (windowHandle == nint.Zero || !NativeMethods.GetWindowRect(windowHandle, out var rect))
        {
            throw new WindowNotFoundException(0);
        }

        return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "capture" : clean;
    }

    private static string GetAvailableFilePath(string outputDirectory, string baseName)
    {
        var candidate = Path.Combine(outputDirectory, $"{baseName}.png");
        for (var suffix = 1; File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(outputDirectory, $"{baseName}_{suffix}.png");
        }

        return candidate;
    }
}
