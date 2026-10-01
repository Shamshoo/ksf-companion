using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace KsfCompanion
{
    /// <summary>
    /// One-click install: the downloaded KSFCompanion.exe copies itself to %LOCALAPPDATA%\Programs\KSF Companion
    /// (no admin rights needed), adds Start menu and desktop shortcuts, starts with Windows, shows up under
    /// Windows' installed apps (to remove it again), and starts from there. Running a newer download updates it.
    /// </summary>
    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KSFCompanion";

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.AppName);
        public static string InstalledExe => Path.Combine(InstallDir, "KSFCompanion.exe");
        static string RunningExe => Process.GetCurrentProcess().MainModule.FileName;
        static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk");
        static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk");

        /// <summary>
        /// A downloaded copy (not the installed one): install it. A "portable.txt" next to the exe keeps it where it is,
        /// and test copies (KSFC_DATA_DIR) are never installed.
        /// </summary>
        public static bool ShouldInstall()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KSFC_DATA_DIR"))) return false;
            var exe = RunningExe;
            if (string.Equals(exe, InstalledExe, StringComparison.OrdinalIgnoreCase)) return false;
            return !File.Exists(Path.Combine(Path.GetDirectoryName(exe), "portable.txt"));
        }

        /// <summary>Installs (or updates) and starts the installed copy. False if it couldn't, with the reason shown.</summary>
        public static bool InstallAndStart(bool background)
        {
            try
            {
                // An older copy running in the tray holds the exe: stop it (it's this app, being updated).
                StopOtherCopies();
                Directory.CreateDirectory(InstallDir);
                File.Copy(RunningExe, InstalledExe, true);
                var version = typeof(Installer).Assembly.GetName().Version;

                CreateShortcut(StartMenuLink, InstalledExe, "");
                CreateShortcut(DesktopLink, InstalledExe, "");
                using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    run.SetValue(Program.AppName, $"\"{InstalledExe}\" --background");
                using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    key.SetValue("DisplayName", Program.AppName);
                    key.SetValue("DisplayVersion", $"{version.Major}.{version.Minor}.{version.Build}");
                    key.SetValue("Publisher", "KSF Companion");
                    key.SetValue("DisplayIcon", InstalledExe);
                    key.SetValue("InstallLocation", InstallDir);
                    key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall-app");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
                }
                Process.Start(new ProcessStartInfo(InstalledExe, background ? "--background" : "") { UseShellExecute = true, WorkingDirectory = InstallDir });
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                MessageBox.Show("Couldn't install KSF Companion: " + ex.Message + "\n\nIt will run from where it is for now.", Program.AppName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        static void StopOtherCopies()
        {
            var me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("KSFCompanion").Where(p => p.Id != me))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { }
            }
        }

        /// <summary>
        /// Windows' "Uninstall": takes KSF Companion out of CS:S (your keys get back what they did), removes the
        /// shortcuts and the app. Your settings and play-later list stay in Documents\KSF Companion.
        /// </summary>
        public static int Uninstall(Settings settings)
        {
            var question = "Remove KSF Companion?\n\nIt also comes out of Counter-Strike: Source (its cfg files go, and your keys get back what they did before). " +
                           "Your settings and play-later list stay in Documents\\KSF Companion.";
            if (MessageBox.Show(question, Program.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return 1;
            if (GameBridge.FindGameProcessId() != 0)
            {
                MessageBox.Show("Close Counter-Strike: Source first, then remove KSF Companion again.", Program.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return 1;
            }
            StopOtherCopies();
            try
            {
                var dir = SteamLocator.FindCstrikeDir(settings.Get("game_dir"));
                if (dir != null) new GameConfig(dir).Uninstall(settings);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            Startup.Set(false);
            foreach (var link in new[] { StartMenuLink, DesktopLink })
                try { if (File.Exists(link)) File.Delete(link); } catch (IOException) { }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            // The running exe can't delete itself: a moment after it exits, cmd removes the folder.
            if (Directory.Exists(InstallDir))
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
            MessageBox.Show("KSF Companion has been removed.", Program.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        /// <summary>A .lnk through the Windows shell (WScript.Shell), without a COM reference.</summary>
        static void CreateShortcut(string path, string target, string arguments)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            var shell = Activator.CreateInstance(shellType);
            try
            {
                var link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var t = link.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { target });
                t.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { arguments });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
                t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { "KSF surf dashboard for CS:S" });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
