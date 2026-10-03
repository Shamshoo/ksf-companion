using System;
using System.IO;

namespace KsfCompanion
{
    /// <summary>The optional "Start when you log in" entry (an XDG autostart file; starts hidden in the tray).</summary>
    static class Startup
    {
        static string ConfigHome
        {
            get
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                return string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config") : xdg;
            }
        }

        public static string EntryPath => Path.Combine(ConfigHome, "autostart", "ksf-companion.desktop");

        public static bool IsEnabled => File.Exists(EntryPath);

        public static void Set(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    if (File.Exists(EntryPath)) File.Delete(EntryPath);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(EntryPath));
                File.WriteAllText(EntryPath, Desktop.Entry(Installer.ExecutablePath, "--background", autostart: true));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Program.Trace("autostart: " + ex.Message);
            }
        }
    }
}
