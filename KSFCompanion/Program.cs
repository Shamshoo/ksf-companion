using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace KsfCompanion
{
    static class Program
    {
        public const string AppName = "KSF Companion";

        /// <summary>Settings and the play-later list live in Documents\KSF Companion so they are easy to find.</summary>
        public static string DataDir
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("KSFC_DATA_DIR");
                return string.IsNullOrWhiteSpace(overridden)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName)
                    : overridden;
            }
        }

        /// <summary>
        /// What's kept from ksf.surf (map pictures, records, the map list, the maps you've finished) lives in
        /// %LOCALAPPDATA%\KSF Companion - or, for a test copy pointed at another data folder, in a "cache" folder inside
        /// that, so it never touches the real ones.
        /// </summary>
        public static string CacheDir => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KSFC_DATA_DIR"))
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName)
            : Path.Combine(DataDir, "cache");

        [STAThread]
        static int Main(string[] args)
        {
            Directory.CreateDirectory(DataDir);
            var settings = new Settings(Path.Combine(DataDir, "settings.ini"));

            // Windows' "Uninstall" for KSF Companion (Settings > Apps).
            if (args.Length > 0 && args[0] == "--uninstall-app") return Installer.Uninstall(settings);
            if (args.Length > 0 && args[0].StartsWith("--") && args[0] != "--background")
                return Cli.Run(args, settings);

            // The downloaded exe installs itself and starts the installed copy (also how a newer download updates it).
            if (Installer.ShouldInstall() && Installer.InstallAndStart(background: args.Contains("--background"))) return 0;

            // One running copy per data folder (a test copy pointed at another folder doesn't clash with the real one).
            var scope = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KSFC_DATA_DIR"))
                ? ""
                : "." + DataDir.ToLowerInvariant().GetHashCode().ToString("x8");
            using var instance = new Mutex(true, @"Local\KSFCompanion.Instance" + scope, out bool firstInstance);
            using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KSFCompanion.Show" + scope);
            if (!firstInstance)
            {
                // Already running in the tray: ask that copy to bring its dashboard up instead.
                showSignal.Set();
                return 0;
            }

            System.Windows.Forms.Application.EnableVisualStyles();
            var app = CreateApplication();
            // Never pop an error dialog over a fullscreen game; write it to a file instead.
            app.DispatcherUnhandledException += (s, e) =>
            {
                LogError(e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogError(e.Exception);
                e.SetObserved();
            };

            Companion companion = null;
            app.Startup += (s, e) => companion = new Companion(settings, showSignal, startHidden: args.Contains("--background"));
            app.Run();
            GC.KeepAlive(companion);
            return 0;
        }

        /// <summary>The WPF application with the dark theme loaded.</summary>
        public static Application CreateApplication()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/KSFCompanion;component/Ui/Theme.xaml", UriKind.Absolute),
            });
            return app;
        }

        static void LogError(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(DataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\r\n\r\n"); }
            catch (IOException) { }
        }

        static readonly bool Tracing = Environment.GetEnvironmentVariable("KSFC_DEBUG") == "1";
        const long TraceLimit = 1024 * 1024;
        static readonly object traceLock = new object();
        static string traceFile;

        /// <summary>
        /// What the app saw and did, for when something goes wrong: always kept (the last 1-2 MB, in the cache
        /// folder), or in the data folder's debug.log when KSFC_DEBUG=1 is set.
        /// </summary>
        public static void Trace(string message)
        {
            lock (traceLock)
            {
                try
                {
                    if (traceFile == null)
                    {
                        var folder = Tracing ? DataDir : CacheDir;
                        Directory.CreateDirectory(folder);
                        traceFile = Path.Combine(folder, "debug.log");
                    }
                    if (!Tracing && File.Exists(traceFile) && new FileInfo(traceFile).Length > TraceLimit)
                    {
                        var old = Path.ChangeExtension(traceFile, ".old.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(traceFile, old);
                    }
                    File.AppendAllText(traceFile, $"{DateTime.Now:HH:mm:ss.fff}  {message}\r\n");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
