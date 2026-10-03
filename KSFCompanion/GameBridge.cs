using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KsfCompanion
{
    enum SendResult { NoGame, NotListening, BadPassword, Timeout, Accepted }

    /// <summary>
    /// Finds the running CS:S and hands it console commands over the game's own remote console (RCON). The game only
    /// listens for that when it's started with -usercon; KSF Companion's autoexec block sets the password and runs
    /// net_start. This never reads or writes game memory - it's the same channel as typing in the console.
    /// </summary>
    static class GameBridge
    {
        // The native Linux game, and the Windows one under Proton/Wine (Wine names the process after the .exe).
        static readonly string[] Executables = { "cstrike_linux64", "cstrike_win64.exe", "cstrike.exe" };
        static int lastPid;

        public static int FindGameProcessId()
        {
            // Lets the app be exercised against a stand-in process without touching a real running game.
            var testProcess = Environment.GetEnvironmentVariable("KSFC_GAME_PROCESS");
            if (lastPid != 0 && IsGame(lastPid, testProcess)) return lastPid;
            lastPid = 0;
            foreach (var dir in SafeDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                if (IsGame(pid, testProcess)) return lastPid = pid;
            }
            return 0;
        }

        static bool IsGame(int pid, string testProcess)
        {
            var args = CommandLine(pid);
            if (args.Length == 0) return false;
            var exe = args[0].Replace('\\', '/');
            var name = exe.Substring(exe.LastIndexOf('/') + 1);
            if (!string.IsNullOrEmpty(testProcess)) return name == testProcess;
            return Executables.Any(e => string.Equals(name, e, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The game's command line: argv[0] and the launch options Steam passed.</summary>
        public static string[] CommandLine(int pid)
        {
            try
            {
                var raw = File.ReadAllBytes($"/proc/{pid}/cmdline");
                return Encoding.UTF8.GetString(raw).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Whether CS:S was started with -usercon (without it, the game never listens for RCON).</summary>
        public static bool HasUserCon(int pid) => CommandLine(pid).Any(a => a.Equals("-usercon", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The TCP port the game is listening on for RCON, read from /proc: the sockets the game holds, matched with the
        /// kernel's list of listening sockets. Null when it isn't listening (no -usercon, or net_start hasn't run).
        /// </summary>
        public static int? ListeningPort(int pid)
        {
            var inodes = new HashSet<string>();
            try
            {
                foreach (var fd in Directory.EnumerateFileSystemEntries($"/proc/{pid}/fd"))
                {
                    var target = new FileInfo(fd).LinkTarget;
                    if (target != null && target.StartsWith("socket:[", StringComparison.Ordinal)) inodes.Add(target.Substring(8).TrimEnd(']'));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
            if (inodes.Count == 0) return null;

            foreach (var table in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
            {
                string[] lines;
                try { lines = File.ReadAllLines(table); }
                catch (IOException) { continue; }
                foreach (var line in lines.Skip(1))
                {
                    // sl local_address rem_address st tx_queue:rx_queue tr:tm->when retrnsmt uid timeout inode
                    var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 10 || f[3] != "0A" || !inodes.Contains(f[9])) continue;
                    var colon = f[1].LastIndexOf(':');
                    if (int.TryParse(f[1].Substring(colon + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var port)) return port;
                }
            }
            return null;
        }

        static IEnumerable<string> SafeDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
        }
    }

    /// <summary>
    /// A Source engine RCON connection to the game on this computer, kept open between commands. Each packet is
    /// [size][id][type][text\0][\0]; the game answers a command with a packet carrying the same id.
    /// </summary>
    sealed class RconClient : IDisposable
    {
        const int Auth = 3, AuthResponse = 2, ExecCommand = 2;
        TcpClient client;
        NetworkStream stream;
        int port, nextId = 1;

        public async Task<SendResult> SendAsync(int pid, string password, string command, int timeoutMs = 1500)
        {
            if (pid == 0) return SendResult.NoGame;
            using var timeout = new CancellationTokenSource(timeoutMs);
            var sent = false;
            try
            {
                if (stream == null)
                {
                    var listening = GameBridge.ListeningPort(pid);
                    if (listening == null) return SendResult.NotListening;
                    var connected = await ConnectAsync(listening.Value, password, timeout.Token).ConfigureAwait(false);
                    if (connected != SendResult.Accepted) return connected;
                }

                // Anything left over from an earlier command's answer.
                while (stream.DataAvailable) await ReadPacketAsync(timeout.Token).ConfigureAwait(false);
                var id = NextId();
                await WritePacketAsync(id, ExecCommand, command, timeout.Token).ConfigureAwait(false);
                sent = true;
                while (true)
                {
                    var (answerId, _, _) = await ReadPacketAsync(timeout.Token).ConfigureAwait(false);
                    if (answerId == id) return SendResult.Accepted;
                }
            }
            catch (OperationCanceledException)
            {
                // No answer in time (the game is busy loading a map). A half-read answer would throw the next one off,
                // so start over with a fresh connection; a command that went out still runs once the game gets to it.
                Close();
                return sent ? SendResult.Timeout : SendResult.NotListening;
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                Program.Trace("rcon: " + ex.Message);
                Close();
                return SendResult.NotListening;
            }
        }

        async Task<SendResult> ConnectAsync(int gamePort, string password, CancellationToken cancel)
        {
            Close();
            client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, gamePort, cancel).ConfigureAwait(false);
            stream = client.GetStream();
            port = gamePort;
            var id = NextId();
            await WritePacketAsync(id, Auth, password ?? "", cancel).ConfigureAwait(false);
            // An empty answer first, then the verdict: our id back, or -1 for a wrong password.
            while (true)
            {
                var (answerId, type, _) = await ReadPacketAsync(cancel).ConfigureAwait(false);
                if (type != AuthResponse) continue;
                if (answerId == id)
                {
                    Program.Trace($"rcon: connected on port {port}");
                    return SendResult.Accepted;
                }
                Program.Trace("rcon: the game turned the password down");
                Close();
                return SendResult.BadPassword;
            }
        }

        int NextId()
        {
            nextId = nextId >= int.MaxValue - 1 ? 1 : nextId + 1;
            return nextId;
        }

        async Task WritePacketAsync(int id, int type, string body, CancellationToken cancel)
        {
            var text = Encoding.UTF8.GetBytes(body);
            var packet = new byte[4 + 4 + 4 + text.Length + 2];
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0), packet.Length - 4);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
            text.CopyTo(packet, 12);
            await stream.WriteAsync(packet, cancel).ConfigureAwait(false);
        }

        async Task<(int Id, int Type, string Body)> ReadPacketAsync(CancellationToken cancel)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, cancel).ConfigureAwait(false);
            var size = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (size < 10 || size > 1 << 20) throw new IOException("malformed RCON packet");
            var data = new byte[size];
            await stream.ReadExactlyAsync(data, cancel).ConfigureAwait(false);
            var id = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0));
            var type = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            return (id, type, Encoding.UTF8.GetString(data, 8, size - 10));
        }

        /// <summary>Drops the connection (the game closed, or it's a new game): the next command connects again.</summary>
        public void Close()
        {
            stream?.Dispose();
            client?.Dispose();
            stream = null;
            client = null;
        }

        public void Dispose() => Close();
    }
}
