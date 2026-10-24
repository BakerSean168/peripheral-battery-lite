using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBatteryLite
{
    public sealed class HidEndpoint
    {
        public string Path;
        public string ProductName;
        public ushort Vid, Pid, UsagePage, Usage;
        public int InputLength, OutputLength, FeatureLength;
        public override string ToString()
        {
            return string.Format("{0:X4}:{1:X4} usage={2:X4}:{3:X4} input={4} output={5} feature={6} product={7}",
                Vid, Pid, UsagePage, Usage, InputLength, OutputLength, FeatureLength, ProductName);
        }
    }

    public static class HidWindows
    {
        private const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HiddAttributes
        {
            public int Size;
            public ushort VendorID, ProductID, VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);
        [DllImport("hid.dll")] private static extern bool HidD_GetProductString(SafeFileHandle device, [Out] byte[] buffer, int length);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HidpCaps capabilities);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr info, IntPtr devInfo, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info, ref SpDeviceInterfaceData data,
            IntPtr buffer, int bufferLength, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

        public static List<HidEndpoint> Enumerate(bool includeOtherVendorDevices = false)
        {
            var found = new List<HidEndpoint>();
            Guid guid; HidD_GetHidGuid(out guid);
            IntPtr deviceSet = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (deviceSet == InvalidHandle) return found;
            try
            {
                for (uint i = 0; i < 1024; ++i)
                {
                    var item = new SpDeviceInterfaceData();
                    item.cbSize = Marshal.SizeOf(typeof(SpDeviceInterfaceData));
                    if (!SetupDiEnumDeviceInterfaces(deviceSet, IntPtr.Zero, ref guid, i, ref item))
                        break;
                    int required;
                    SetupDiGetDeviceInterfaceDetail(deviceSet, ref item, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required < 8 || required > 65536) continue;
                    IntPtr buffer = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(deviceSet, ref item, buffer, required, out required, IntPtr.Zero))
                            continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                        if (string.IsNullOrEmpty(path)) continue;
                        string lower = path.ToLowerInvariant();
                        bool known = lower.Contains("vid_373b&pid_1303") ||
                            lower.Contains("vid_320f&pid_5055") || lower.Contains("vid_320f&pid_5088") ||
                            lower.Contains("vid_1038&pid_2232");
                        if (!known && !includeOtherVendorDevices) continue;
                        using (var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                        {
                            if (handle.IsInvalid) continue;
                            var attrs = new HiddAttributes { Size = Marshal.SizeOf(typeof(HiddAttributes)) };
                            if (!HidD_GetAttributes(handle, ref attrs)) continue;
                            if (!known && !includeOtherVendorDevices) continue;
                            IntPtr prep;
                            if (!HidD_GetPreparsedData(handle, out prep)) continue;
                            try
                            {
                                var caps = new HidpCaps();
                                if (HidP_GetCaps(prep, ref caps) != 0x00110000) continue;
                                // Extension discovery considers only vendor-defined HID collections.
                                if (!known && caps.UsagePage < 0xFF00) continue;
                                string product = "";
                                byte[] strBuf = new byte[256];
                                if (HidD_GetProductString(handle, strBuf, strBuf.Length))
                                {
                                    product = System.Text.Encoding.Unicode.GetString(strBuf);
                                    int end = product.IndexOf((char)0);
                                    if (end >= 0) product = product.Substring(0, end);
                                }
                                found.Add(new HidEndpoint
                                {
                                    Path = path, ProductName = product, Vid = attrs.VendorID, Pid = attrs.ProductID,
                                    UsagePage = caps.UsagePage, Usage = caps.Usage,
                                    InputLength = caps.InputReportByteLength,
                                    OutputLength = caps.OutputReportByteLength,
                                    FeatureLength = caps.FeatureReportByteLength
                                });
                            }
                            finally { HidD_FreePreparsedData(prep); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(deviceSet); }
            return found;
        }

        // Only the two known battery-read commands are allowed; no firmware/config writes.
        // Blocking reads are cancelled when a sleeping wireless device does not reply.
        public static byte[] Exchange(HidEndpoint ep, byte[] request, Func<byte[], bool> replyMatches, int timeoutMs = 650)
        {
            if (request.Length != ep.OutputLength || ep.InputLength <= 0)
                throw new InvalidOperationException("HID report size does not match descriptor");
            using (var handle = CreateFile(ep.Path, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    throw new IOException("Cannot open vendor HID interface (device may be busy)");
                using (var stream = new FileStream(handle, FileAccess.ReadWrite, Math.Max(64, ep.InputLength), true))
                {
                    // Vendor HID writes can stall when another driver/application holds the endpoint.
                    // Use overlapped writes and cancel on timeout, never block the UI polling thread.
                    IAsyncResult write = stream.BeginWrite(request, 0, request.Length, null, null);
                    if (!write.AsyncWaitHandle.WaitOne(timeoutMs))
                    {
                        CancelIoEx(handle, IntPtr.Zero);
                        throw new TimeoutException("HID write timed out");
                    }
                    stream.EndWrite(write);
                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline)
                    {
                        var bytes = new byte[ep.InputLength];
                        IAsyncResult ar = stream.BeginRead(bytes, 0, bytes.Length, null, null);
                        int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                        bool completed = ar.AsyncWaitHandle.WaitOne(remaining);
                        if (!completed)
                        {
                            CancelIoEx(handle, IntPtr.Zero);
                            // Do not call EndRead while pending: some drivers fail to signal cancellation.
                            return null;
                        }
                        int size = stream.EndRead(ar);
                        if (size <= 0) return null;
                        if (size != bytes.Length) Array.Resize(ref bytes, size);
                        if (replyMatches(bytes)) return bytes;
                    }
                    return null;
                }
            }
        }
    }
}