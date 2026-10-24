using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeripheralBatteryLite
{
    internal static class StartupRegistration
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistryValue = "PeripheralBatteryLite";

        public static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    return key != null && key.GetValue(RegistryValue) != null;
            }
        }

        public static bool SetEnabled(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
                {
                    if (key == null) throw new IOException("CurrentUser Run registry key unavailable");
                    if (enabled) key.SetValue(RegistryValue, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(RegistryValue, false);
                }
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }
    }
}
