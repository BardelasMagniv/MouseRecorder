using System;

namespace MouseRecorder
{
    internal class SessionMetadata
    {
        public Guid SessionId { get; set; }
        public DateTimeOffset SessionStartUtc { get; set; }
        public string RecordingMode { get; set; } = "Active";
        public bool IsPassive { get; set; } = false;
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int DpiX { get; set; }
        public int DpiY { get; set; }
    }
}