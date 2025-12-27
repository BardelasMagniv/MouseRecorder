using System;
using System.Runtime.InteropServices;

namespace MouseRecorder
{
    internal class RawInputManager
    {
        public event EventHandler<RawMouseEventArgs>? RawInputReceived;
        private bool _registered = false;


        public void RegisterRawInput(IntPtr hwnd, bool receiveInBackground)
        {
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01;
            rid[0].usUsage = 0x02; // mouse
            rid[0].dwFlags = receiveInBackground ? RawInputConstants.RIDEV_INPUTSINK : 0;
            rid[0].hwndTarget = hwnd;

            if (!RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            {
                int err = Marshal.GetLastWin32Error();
            }
            else
            {
                _registered = true;
            }
        }

        public void UnregisterRawInput()
        {
            if (!_registered) return;
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01;
            rid[0].usUsage = 0x02;
            rid[0].dwFlags = RawInputConstants.RIDEV_REMOVE;
            rid[0].hwndTarget = IntPtr.Zero;
            RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            _registered = false;
        }

        public void HandleRawInput(IntPtr hRawInput)
        {
            try
            {
                uint dwSize = 0;
                uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
                uint res = GetRawInputData(hRawInput, RawInputConstants.RID_INPUT, IntPtr.Zero, ref dwSize, headerSize);
                if (dwSize == 0) return;
                IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
                try
                {
                    uint read = GetRawInputData(hRawInput, RawInputConstants.RID_INPUT, buffer, ref dwSize, headerSize);
                    if (read == 0) return;

                    // Read header
                    RAWINPUTHEADER header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
                    if (header.dwType != RawInputConstants.RIM_TYPEMOUSE) return;

                    int headerStructSize = Marshal.SizeOf<RAWINPUTHEADER>();
                    IntPtr mousePtr = IntPtr.Add(buffer, headerStructSize);
                    RAWMOUSE mouse = Marshal.PtrToStructure<RAWMOUSE>(mousePtr);

                    var args = new RawMouseEventArgs
                    {
                        RawDx = mouse.lLastX,
                        RawDy = mouse.lLastY,
                        ButtonFlags = mouse.usButtonFlags,
                        WheelDelta = (short)mouse.usButtonData,
                        DeviceHandle = header.hDevice,
                        DeviceName = GetDeviceName(header.hDevice)
                    };

                    RawInputReceived?.Invoke(this, args);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                // swallow in prototype
            }
        }

        private string GetDeviceName(IntPtr hDevice)
        {
            uint size = 0;
            GetRawInputDeviceInfo(hDevice, RawInputConstants.RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size == 0) return string.Empty;
            IntPtr buffer = Marshal.AllocHGlobal((int)(size * 2));
            try
            {
                uint read = GetRawInputDeviceInfo(hDevice, RawInputConstants.RIDI_DEVICENAME, buffer, ref size);
                string name = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                return name;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // P/Invoke
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RAWMOUSE { public ushort usFlags; public uint ulButtons; public ushort usButtonFlags; public ushort usButtonData; public uint ulRawButtons; public int lLastX; public int lLastY; public uint ulExtraInformation; }
    }
}
