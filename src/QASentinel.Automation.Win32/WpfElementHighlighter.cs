using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;

namespace QASentinel.Automation.Win32;

public sealed class WpfElementHighlighter : IElementHighlighter
{
    private readonly TaskCompletionSource<(Dispatcher Dispatcher, Window Window, nint Handle)> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private bool _disposed;

    public WpfElementHighlighter()
    {
        _thread = new Thread(RunOverlay)
        {
            IsBackground = true,
            Name = "QA Sentinel Highlight Overlay"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public async Task ShowAsync(BoundingRectangle bounds, CancellationToken cancellationToken)
    {
        if (bounds.IsEmpty || _disposed)
        {
            return;
        }

        var state = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await state.Dispatcher.InvokeAsync(() =>
        {
            var x = checked((int)Math.Round(bounds.X));
            var y = checked((int)Math.Round(bounds.Y));
            var width = Math.Max(1, checked((int)Math.Round(bounds.Width)));
            var height = Math.Max(1, checked((int)Math.Round(bounds.Height)));
            _ = NativeMethods.SetWindowPos(
                state.Handle,
                NativeMethods.HwndTopmost,
                x,
                y,
                width,
                height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }, DispatcherPriority.Send, cancellationToken);
    }

    public async Task HideAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        var state = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await state.Dispatcher.InvokeAsync(state.Window.Hide, DispatcherPriority.Send, cancellationToken);
    }

    private void RunOverlay()
    {
        try
        {
            var window = new Window
            {
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowActivated = false,
                ShowInTaskbar = false,
                Topmost = true,
                Focusable = false,
                Width = 1,
                Height = 1,
                Content = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x7A, 0xFF)),
                    BorderThickness = new Thickness(3),
                    Background = new SolidColorBrush(Color.FromArgb(24, 0x16, 0x7A, 0xFF)),
                    IsHitTestVisible = false
                }
            };

            window.SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                var style = NativeMethods.GetWindowLongPtrW(handle, NativeMethods.GwlExStyle).ToInt64();
                style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
                _ = NativeMethods.SetWindowLongPtrW(handle, NativeMethods.GwlExStyle, new nint(style));
                window.Hide();
                _ready.TrySetResult((Dispatcher.CurrentDispatcher, window, handle));
            };

            window.Show();
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ready.Task.IsCompletedSuccessfully)
        {
            var state = await _ready.Task.ConfigureAwait(false);
            await state.Dispatcher.InvokeAsync(() =>
            {
                state.Window.Close();
                state.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            });
        }

        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
