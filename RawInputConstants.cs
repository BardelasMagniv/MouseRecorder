namespace MouseRecorder
{
    internal static class RawInputConstants
    {
        public const uint RID_INPUT = 0x10000003;
        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIM_TYPEMOUSE = 0;

        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_REMOVE = 0x00000001;

        // Mouse button flags
        public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
        public const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
        public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
        public const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
        public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
        public const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
        public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
        public const ushort RI_MOUSE_BUTTON_4_UP = 0x0080;
        public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
        public const ushort RI_MOUSE_BUTTON_5_UP = 0x0200;
        public const ushort RI_MOUSE_WHEEL = 0x0400;
        public const ushort RI_MOUSE_HWHEEL = 0x0800;
    }
}
