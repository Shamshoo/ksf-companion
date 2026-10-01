using System.Windows.Forms;
using Microsoft.Win32;

namespace KsfCompanion
{
    /// <summary>The optional "Start with Windows" entry (HKCU Run key, starts hidden in the tray).</summary>
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "KSF Companion";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --background");
            else key.DeleteValue(ValueName, false);
        }
    }
}
