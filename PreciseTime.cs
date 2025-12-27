using System;
using System.Runtime.InteropServices;

namespace MouseRecorder
{
    internal static class PreciseTime
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void GetSystemTimePreciseAsFileTime(out FILETIME lpSystemTimeAsFileTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        public static DateTimeOffset GetSystemTimePreciseUtc()
        {
            try
            {
                GetSystemTimePreciseAsFileTime(out FILETIME ft);
                long fileTime = ((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
                var dt = DateTime.FromFileTimeUtc(fileTime);
                return new DateTimeOffset(dt);
            }
            catch
            {
                // Fallback to system time if API not available
                return DateTimeOffset.UtcNow;
            }
        }
    }
}
