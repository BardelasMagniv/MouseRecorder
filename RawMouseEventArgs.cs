using System;

namespace MouseRecorder
{
    internal class RawMouseEventArgs : EventArgs
    {
        public int RawDx { get; set; }
        public int RawDy { get; set; }
        public ushort ButtonFlags { get; set; }
        public short WheelDelta { get; set; }
        public IntPtr DeviceHandle { get; set; }
        public string DeviceName { get; set; } = string.Empty;
    }
}
