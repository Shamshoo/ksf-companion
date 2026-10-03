using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using KsfCompanion.Ui;

namespace KsfCompanion
{
    static class Program
    {
        public const string AppName = "KSF Companion";

        /// <summary>Settings and the play-later list live in Documents/KSF Companion so they are easy to find.</summary>
        public static string DataDir
        {
            get
            {
                var overridden = Environment.GetEnvironmentVariable("KSFC_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(documents)) documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(documents, AppName);
            }
        }

        /// <summary>
        /// What's kept from ksf.surf (map pictures, records, the map list, the maps you've finished) lives in
        /// ~/.cache/ksf-companion - or, for a test copy pointed at another data folder, in a "cache" folder inside
        /// that, so it never touches the real ones.
        /// </summary>
        public static string CacheDir
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KSFC_DATA_DIR"))) return Path.Combine(DataDir, "cache");
                var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                var cache = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache") : xdg;
                return Path.Combine(cache, "ksf-companion");
            }
        }

        enum Mode { Run, Install, Uninstall }
        static Mode mode;
        static Settings settings;
        static string[] arguments;
        static InstanceGuard guard;

        static int Main(string[] args)
        {
            Directory.CreateDirectory(DataDir);
            settings = new Settings(Path.Combine(DataDir, "settings.ini"));
            arguments = args;

            // "Uninstall KSF Companion" in the app menu.
            if (args.Length > 0 && args[0] == "--uninstall-app") mode = Mode.Uninstall;
            else if (args.Length > 0 && args[0].StartsWith("--") && args[0] != "--background") return Cli.Run(args, settings);
            // The downloaded file installs itself and starts the installed copy (also how a newer download updates it).
            else if (Installer.ShouldInstall()) mode = Mode.Install;
            else
            {
                // One running copy per data folder (a test copy pointed at another folder doesn't clash with the real one).
                guard = InstanceGuard.TryAcquire();
                if (guard == null) return 0;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogError(e.Exception);
                e.SetObserved();
            };
            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            }
            finally
            {
                guard?.Dispose();
            }
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new X11PlatformOptions { WmClass = "KSFCompanion" })
            .LogToTrace();

        /// <summary>Called once Avalonia is up: install, uninstall or run in the tray.</summary>
        internal static async void Start(IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Never pop an error dialog over a fullscreen game; write it to a file instead.
            Dispatcher.UIThread.UnhandledException += (s, e) =>
            {
                LogError(e.Exception);
                e.Handled = true;
            };
            switch (mode)
            {
                case Mode.Uninstall:
                    desktop.Shutdown(await Installer.UninstallAsync(settings));
                    return;
                case Mode.Install:
                    if (await Installer.InstallAndStartAsync(background: arguments.Contains("--background")))
                    {
                        desktop.Shutdown();
                        return;
                    }
                    guard = InstanceGuard.TryAcquire();
                    if (guard == null)
                    {
                        desktop.Shutdown();
                        return;
                    }
                    break;
            }
            var companion = new Companion(settings, guard, startHidden: arguments.Contains("--background"));
            GC.KeepAlive(companion);
        }

        static void LogError(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(DataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\n\n"); }
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
                    File.AppendAllText(traceFile, $"{DateTime.Now:HH:mm:ss.fff}  {message}\n");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>The Avalonia application with the dark theme loaded.</summary>
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            // Started from inside the main loop, so installing (which quits straight away) can shut it down.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) Dispatcher.UIThread.Post(() => Program.Start(desktop));
            base.OnFrameworkInitializationCompleted();
        }
    }

    /// <summary>
    /// One running copy at a time: the first one listens on a socket in the runtime folder; starting another one just
    /// tells the first to bring its dashboard up, and quits.
    /// </summary>
    sealed class InstanceGuard : IDisposable
    {
        readonly Socket listener;
        readonly string path;

        public event Action ShowRequested;

        InstanceGuard(Socket listener, string path)
        {
            this.listener = listener;
            this.path = path;
            if (listener != null) new Thread(Listen) { IsBackground = true, Name = "instance" }.Start();
        }

        static string SocketPath
        {
            get
            {
                var data = Environment.GetEnvironmentVariable("KSFC_DATA_DIR");
                var scope = string.IsNullOrWhiteSpace(data) ? "" : "." + StableHash(Program.DataDir.ToLowerInvariant()).ToString("x8");
                var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                var path = string.IsNullOrWhiteSpace(runtime) || !Directory.Exists(runtime) ? null : Path.Combine(runtime, "ksf-companion" + scope + ".sock");
                // Socket paths can't be longer than about 100 characters; /tmp (with your user name in it) always fits.
                if (path == null || path.Length > 100)
                    path = Path.Combine(Path.GetTempPath(), $"ksf-companion-{Environment.UserName}{scope}.sock");
                return path;
            }
        }

        static uint StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (var c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }

        /// <summary>This copy's guard, or null when another copy is running (it has been asked to show its dashboard).</summary>
        public static InstanceGuard TryAcquire()
        {
            var path = SocketPath;
            try
            {
                using var other = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                other.Connect(new UnixDomainSocketEndPoint(path));
                other.Send(Encoding.ASCII.GetBytes("show\n"));
                return null;
            }
            catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
            {
                // Nobody's listening: a copy that crashed left the file behind.
                try { File.Delete(path); } catch (IOException) { }
            }
            try
            {
                var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listener.Bind(new UnixDomainSocketEndPoint(path));
                listener.Listen(4);
                return new InstanceGuard(listener, path);
            }
            catch (Exception ex) when (ex is SocketException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                // Better a second copy than none at all.
                Program.Trace("single instance: " + ex.Message);
                return new InstanceGuard(null, null);
            }
        }

        void Listen()
        {
            var buffer = new byte[16];
            while (true)
            {
                try
                {
                    using var client = listener.Accept();
                    client.ReceiveTimeout = 2000;
                    var n = client.Receive(buffer);
                    if (n > 0 && Encoding.ASCII.GetString(buffer, 0, n).StartsWith("show")) ShowRequested?.Invoke();
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
            }
        }

        public void Dispose()
        {
            if (listener == null) return;
            listener.Dispose();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
