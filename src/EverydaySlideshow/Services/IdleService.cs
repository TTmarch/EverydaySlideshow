using System.Runtime.InteropServices;

namespace EverydaySlideshow.Services;

public static class IdleService
{
    public static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo
        {
            CbSize = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var idleMilliseconds = Environment.TickCount64 - info.DwTime;
        return TimeSpan.FromMilliseconds(Math.Max(0, idleMilliseconds));
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }
}
