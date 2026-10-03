namespace QASentinel.Automation.Win32;

public interface ICursorService
{
    (double X, double Y) GetPosition();
}

public sealed class CursorService : ICursorService
{
    public (double X, double Y) GetPosition()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        return (point.X, point.Y);
    }
}
