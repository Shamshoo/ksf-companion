using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace KsfCompanion
{
    /// <summary>The Linux desktop's ways of doing things: opening links and folders, notifications, .desktop entries.</summary>
    static class Desktop
    {
        public const string IconName = "ksf-companion";

        /// <summary>Opens a web page, a steam:// link or a folder with whatever the desktop uses for it.</summary>
        public static bool Open(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            return Run("xdg-open", target);
        }

        /// <summary>A desktop notification (the tray balloon on Windows). Quietly does nothing without notify-send.</summary>
        public static void Notify(string title, string text)
        {
            Run("notify-send", "--app-name=" + Program.AppName, "--icon=" + IconName, title, text);
        }

        static bool Run(string program, params string[] args)
        {
            try
            {
                var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in args) info.ArgumentList.Add(arg);
                using var process = Process.Start(info);
                return process != null;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                Program.Trace($"{program}: {ex.Message}");
                return false;
            }
        }

        /// <summary>A .desktop file that starts KSF Companion (for the app menu, or for starting when you log in).</summary>
        public static string Entry(string executable, string arguments, bool autostart)
        {
            var exec = Quote(executable) + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments);
            var sb = new StringBuilder();
            sb.Append("[Desktop Entry]\n");
            sb.Append("Type=Application\n");
            sb.Append("Name=").Append(Program.AppName).Append('\n');
            sb.Append("Comment=KSF surf dashboard for Counter-Strike: Source\n");
            sb.Append("Exec=").Append(exec).Append('\n');
            sb.Append("Icon=").Append(IconName).Append('\n');
            sb.Append("Terminal=false\n");
            sb.Append("Categories=Game;Utility;\n");
            sb.Append("StartupWMClass=KSFCompanion\n");
            if (autostart)
            {
                sb.Append("X-GNOME-Autostart-enabled=true\n");
                sb.Append("X-KDE-autostart-after=panel\n");
            }
            else
            {
                // Right-click it in the app menu: take KSF Companion off this computer again.
                sb.Append("Actions=uninstall;\n\n");
                sb.Append("[Desktop Action uninstall]\n");
                sb.Append("Name=Uninstall KSF Companion\n");
                sb.Append("Exec=").Append(Quote(executable)).Append(" --uninstall-app\n");
            }
            return sb.ToString();
        }

        /// <summary>One argument of an Exec= line, quoted the way the Desktop Entry spec wants when it has to be.</summary>
        static string Quote(string arg)
        {
            var plain = arg.Length > 0;
            foreach (var c in arg)
                if (!(char.IsLetterOrDigit(c) || c == '/' || c == '.' || c == '-' || c == '_' || c == '+')) plain = false;
            if (plain) return arg;
            var sb = new StringBuilder("\"");
            foreach (var c in arg)
            {
                // Inside quotes ", `, $ and \ take a backslash, and every backslash is doubled again for the file format.
                if (c == '"' || c == '`' || c == '$') sb.Append("\\\\").Append(c);
                else if (c == '\\') sb.Append("\\\\\\\\");
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
