using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KsfCompanion.Ui;

namespace KsfCompanion
{
    /// <summary>
    /// One-click install: the downloaded KSFCompanion file copies itself to ~/.local/share/ksf-companion (no root
    /// needed), adds itself to the app menu (right-click it there to uninstall), starts when you log in, and starts from
    /// there. Running a newer download updates it.
    /// </summary>
    static class Installer
    {
        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        static string DataHome
        {
            get
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                return string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Home, ".local", "share") : xdg;
            }
        }

        public static string InstallDir => Path.Combine(DataHome, "ksf-companion");
        public static string InstalledExe => Path.Combine(InstallDir, "KSFCompanion");
        static string RunningExe => Environment.ProcessPath;
        static string MenuEntry => Path.Combine(DataHome, "applications", "ksf-companion.desktop");
        static string IconFile => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", Desktop.IconName + ".png");

        /// <summary>The copy that should start with your login: the installed one if there is one.</summary>
        public static string ExecutablePath => File.Exists(InstalledExe) ? InstalledExe : RunningExe;

        /// <summary>
        /// A downloaded copy (not the installed one): install it. A "portable.txt" next to it keeps it where it is; test
        /// copies (KSFC_DATA_DIR) and builds run from the source folder (not one self-contained file) are never installed.
        /// </summary>
        public static bool ShouldInstall()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KSFC_DATA_DIR"))) return false;
            // Only the single-file build carries everything it needs; a dotnet build output is a folder of files.
            if (!string.IsNullOrEmpty(typeof(Installer).Assembly.Location)) return false;
            var exe = RunningExe;
            if (string.IsNullOrEmpty(exe) || string.Equals(exe, InstalledExe, StringComparison.Ordinal)) return false;
            return !File.Exists(Path.Combine(Path.GetDirectoryName(exe), "portable.txt"));
        }

        /// <summary>Installs (or updates) and starts the installed copy. False if it couldn't, with the reason shown.</summary>
        public static async Task<bool> InstallAndStartAsync(bool background)
        {
            try
            {
                // An older copy running in the tray: stop it (it's this app, being updated).
                StopOtherCopies();
                Directory.CreateDirectory(InstallDir);
                // Copied next to it and then moved over it, so a running old copy is never written into.
                var temp = InstalledExe + ".new";
                File.Copy(RunningExe, temp, true);
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.Move(temp, InstalledExe, true);

                WriteIcon();
                Directory.CreateDirectory(Path.GetDirectoryName(MenuEntry));
                File.WriteAllText(MenuEntry, Desktop.Entry(InstalledExe, "", autostart: false));
                Startup.Set(true);
                RefreshMenus();

                var start = new ProcessStartInfo(InstalledExe) { UseShellExecute = false, WorkingDirectory = InstallDir };
                if (background) start.ArgumentList.Add("--background");
                Process.Start(start);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                await Dialog.ShowAsync("Couldn't install KSF Companion: " + ex.Message + "\n\nIt will run from where it is for now.");
                return false;
            }
        }

        static void WriteIcon()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IconFile));
                using var source = Avalonia.Platform.AssetLoader.Open(new Uri("avares://KSFCompanion/Ui/Assets/icon-256.png"));
                using var target = File.Create(IconFile);
                source.CopyTo(target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Program.Trace("icon: " + ex.Message);
            }
        }

        /// <summary>Asks the app menu to pick up the new entry (it usually notices by itself).</summary>
        static void RefreshMenus()
        {
            foreach (var tool in new[] { "update-desktop-database", "kbuildsycoca6", "kbuildsycoca5" })
            {
                try
                {
                    var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    if (tool == "update-desktop-database") info.ArgumentList.Add(Path.GetDirectoryName(MenuEntry));
                    using var _ = Process.Start(info);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { }
            }
        }

        static void StopOtherCopies()
        {
            var me = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("KSFCompanion").Where(p => p.Id != me))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { }
                finally { p.Dispose(); }
            }
        }

        /// <summary>
        /// "Uninstall KSF Companion" (right-click it in the app menu): takes KSF Companion out of CS:S (your keys get back
        /// what they did), removes it from the menu and from your login, and deletes the app. Your settings and play-later
        /// list stay in Documents/KSF Companion.
        /// </summary>
        public static async Task<int> UninstallAsync(Settings settings)
        {
            var question = "Remove KSF Companion?\n\nIt also comes out of Counter-Strike: Source (its cfg files go, and your keys get back what they did before). " +
                           "Your settings and play-later list stay in " + Program.DataDir + ".";
            if (!await Dialog.AskAsync(question, "Remove")) return 1;
            if (GameBridge.FindGameProcessId() != 0)
            {
                await Dialog.ShowAsync("Close Counter-Strike: Source first, then remove KSF Companion again.");
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
            foreach (var file in new[] { MenuEntry, IconFile })
                try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
            // A running program's file can be deleted on Linux; it's gone once this copy exits.
            try { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            RefreshMenus();
            await Dialog.ShowAsync("KSF Companion has been removed.");
            return 0;
        }
    }
}
