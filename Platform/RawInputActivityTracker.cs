using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PeripheralBatteryLite
{
    // Passive Windows Raw Input notifications. Reads only device identifiers and
    // the RAWINPUTHEADER; it never collects keystrokes, mouse positions or text.
    // Does not send any HID messages and cannot keep a device awake.
    internal sealed class RawInputActivityTracker : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RID_HEADER = 0x10000005;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIM_TYPEMOUSE = 0;
        private const uint RIM_TYPEKEYBOARD = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            [In] RAWINPUTDEVICE[] devices, uint count, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr input, uint command,
            out RAWINPUTHEADER data, ref uint size, uint headerSize);

        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW",
            CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(
            IntPtr device, uint command, StringBuilder name, ref uint size);

        private readonly Dictionary<IntPtr, ActivityCategory> kindByHandle =
            new Dictionary<IntPtr, ActivityCategory>();
        private DateTime? lastMouseUtc;
        private DateTime? lastKeyboardUtc;
        private bool registered;

        public bool IsRegistered { get { return registered; } }

        public bool Register(IntPtr hwnd)
        {
            if (registered) return true;
            if (hwnd == IntPtr.Zero) return false;
            var devices = new[]
            {
                new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2,
                    dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd },
                new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6,
                    dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd }
            };
            registered = RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            return registered;
        }

        public void ProcessMessage(int message, IntPtr lParam)
        {
            if (!registered || message != WM_INPUT) return;
            var header = new RAWINPUTHEADER();
            uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint count = GetRawInputData(lParam, RID_HEADER, out header, ref size,
                (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
            if (count == unchecked((uint)-1) || count < size || header.hDevice == IntPtr.Zero)
                return;
            ActivityCategory kind;
            if (!kindByHandle.TryGetValue(header.hDevice, out kind))
            {
                string device = GetName(header.hDevice);
                kind = ActivityCategory.Other;
                if (header.dwType == RIM_TYPEKEYBOARD &&
                    device.IndexOf("vid_320f&pid_5088", StringComparison.OrdinalIgnoreCase) >= 0)
                    kind = ActivityCategory.Keyboard;
                else if (header.dwType == RIM_TYPEMOUSE &&
                    device.IndexOf("vid_373b&pid_1303", StringComparison.OrdinalIgnoreCase) >= 0)
                    kind = ActivityCategory.Mouse;
                kindByHandle[header.hDevice] = kind;
            }
            if (kind == ActivityCategory.Keyboard) lastKeyboardUtc = DateTime.UtcNow;
            else if (kind == ActivityCategory.Mouse) lastMouseUtc = DateTime.UtcNow;
        }

        private static string GetName(IntPtr device)
        {
            uint chars = 0;
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref chars);
            if (chars == 0 || chars > 8192) return "";
            var buffer = new StringBuilder((int)chars + 1);
            uint result = GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref chars);
            return result == unchecked((uint)-1) ? "" : buffer.ToString();
        }

        public bool RecentlyActive(ActivityCategory category, DateTime nowUtc)
        {
            DateTime? value = category == ActivityCategory.Keyboard ? lastKeyboardUtc :
                category == ActivityCategory.Mouse ? lastMouseUtc : null;
            return value.HasValue && nowUtc - value.Value < TimeSpan.FromMinutes(3);
        }

        // Turning off automatic polling must also turn off OS-wide Raw Input.
        // Reset remembered activity so re-enabling does not reuse stale events.
        public bool Unregister()
        {
            if (!registered)
            {
                kindByHandle.Clear();
                lastKeyboardUtc = null;
                lastMouseUtc = null;
                return true;
            }
            var devices = new[]
            {
                new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2,
                    dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
                new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6,
                    dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero }
            };
            bool success = RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            if (success)
            {
                registered = false;
                kindByHandle.Clear();
                lastKeyboardUtc = null;
                lastMouseUtc = null;
            }
            return success;
        }

        public void Dispose() { Unregister(); }
    }
}
