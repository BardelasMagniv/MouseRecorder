using System;

namespace MouseRecorder
{
    internal class MouseEvent
    {
        public long EventIndex { get; set; }
        public string EventType { get; set; } = string.Empty; // move/down/up/wheel/trial_spawn/trial_hit/session_start/session_end
        public int? TrialId { get; set; }
        public string? Button { get; set; }
        public string? ButtonState { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public float? NormX { get; set; }
        public float? NormY { get; set; }
        public int? RawDx { get; set; }
        public int? RawDy { get; set; }
        public int? WheelDelta { get; set; }
        public long TsMonotonicUs { get; set; }
        public string? TsUtcIso { get; set; }
        public long? QpcTicks { get; set; }
        public int? ScreenW { get; set; }
        public int? ScreenH { get; set; }
        public int? WindowLeft { get; set; }
        public int? WindowTop { get; set; }
        public int? WindowW { get; set; }
        public int? WindowH { get; set; }
        public string? CoordinateSpace { get; set; }
        public string? DeviceName { get; set; }
        public int? SpawnW { get; set; }
        public int? SpawnH { get; set; }
        public int? SpawnMargin { get; set; }
        public string? Comment { get; set; }
        public Guid? SessionId { get; set; }
    }
}
