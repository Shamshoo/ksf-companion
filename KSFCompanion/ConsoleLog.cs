using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KsfCompanion
{
    /// <summary>Follows the game's console log files and reports each new line.</summary>
    sealed class LogWatcher
    {
        sealed class Tail
        {
            public string Path;
            public long Position;
            public readonly Decoder Decoder = new UTF8Encoding(false).GetDecoder();
            public readonly StringBuilder Partial = new StringBuilder();
        }

        readonly List<Tail> tails;
        readonly byte[] bytes = new byte[64 * 1024];
        readonly char[] chars = new char[64 * 1024 + 16];

        public event Action<string> LineRead;
        public DateTime LastLineAt { get; private set; } = DateTime.MinValue;

        /// <param name="startAt">Where to start in a given file; by default whatever is already in a log is skipped
        /// (a log created later is read from the start).</param>
        public LogWatcher(IEnumerable<string> paths, IDictionary<string, long> startAt = null)
        {
            tails = paths.Select(p => new Tail
            {
                Path = p,
                Position = startAt != null && startAt.TryGetValue(p, out var start) ? start : Length(p),
            }).ToList();
        }

        public void Poll()
        {
            foreach (var tail in tails) Read(tail);
        }

        /// <summary>How far into <paramref name="path"/> has been read, if that is at the start of a line.</summary>
        public bool TryGetCheckpoint(string path, out long position)
        {
            var tail = tails.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            position = tail?.Position ?? 0;
            return tail != null && tail.Partial.Length == 0;
        }

        void Read(Tail tail)
        {
            FileStream stream;
            try
            {
                stream = new FileStream(tail.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (FileNotFoundException) { tail.Position = 0; return; }
            catch (DirectoryNotFoundException) { tail.Position = 0; return; }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            using (stream)
            {
                var length = stream.Length;
                if (length < tail.Position)
                {
                    tail.Position = 0;
                    tail.Decoder.Reset();
                    tail.Partial.Clear();
                }
                if (length == tail.Position) return;

                stream.Seek(tail.Position, SeekOrigin.Begin);
                var budget = 4 * 1024 * 1024;
                int read;
                while (budget > 0 && (read = stream.Read(bytes, 0, bytes.Length)) > 0)
                {
                    tail.Position += read;
                    budget -= read;
                    Emit(tail, tail.Decoder.GetChars(bytes, 0, read, chars, 0));
                }
            }
        }

        void Emit(Tail tail, int count)
        {
            var start = 0;
            for (var i = 0; i < count; i++)
            {
                if (chars[i] != '\n') continue;
                tail.Partial.Append(chars, start, i - start);
                var line = tail.Partial.ToString();
                tail.Partial.Clear();
                start = i + 1;
                LastLineAt = DateTime.Now;
                LineRead?.Invoke(line);
            }
            if (start < count) tail.Partial.Append(chars, start, count - start);
            if (tail.Partial.Length > 16 * 1024) tail.Partial.Clear();
        }

        static long Length(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (IOException) { return 0; }
        }

        /// <summary>
        /// The map the running game is on, read from the end of the newest log. Anything before the
        /// companion's "ready" line belongs to an earlier game session and is ignored.
        /// </summary>
        public static string FindCurrentMap(IEnumerable<string> paths)
        {
            var file = paths.Where(File.Exists).OrderByDescending(p => new FileInfo(p).LastWriteTimeUtc).FirstOrDefault();
            if (file == null) return null;

            string text;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var take = (int)Math.Min(stream.Length, 2 * 1024 * 1024);
                stream.Seek(-take, SeekOrigin.End);
                var buffer = new byte[take];
                var total = 0;
                while (total < take)
                {
                    var n = stream.Read(buffer, total, take - total);
                    if (n <= 0) break;
                    total += n;
                }
                text = Encoding.UTF8.GetString(buffer, 0, total);
            }
            catch (IOException) { return null; }

            string map = null;
            var parser = new LogParser();
            parser.MapChanged += m => map = m;
            parser.GameStarted += () => map = null;
            foreach (var line in text.Split('\n')) parser.Feed(line);
            return map;
        }
    }

    /// <summary>Recognises the few console lines KSF Companion cares about.</summary>
    sealed class LogParser
    {
        static readonly Regex Timestamp = new Regex(@"^(?:L )?\d\d/\d\d/\d{4} - \d\d:\d\d:\d\d: ", RegexOptions.Compiled);
        // The engine prints "Map: <name>" then "Players: n / max" whenever the client joins a map.
        static readonly Regex MapLine = new Regex(@"^Map: ([A-Za-z0-9_\-.]+)$", RegexOptions.Compiled);
        static readonly Regex PlayersLine = new Regex(@"^Players: \d+ / \d+", RegexOptions.Compiled);

        public event Action<string> MapChanged;
        public event Action SaveRequested;
        public event Action GameStarted;
        /// <summary>The map finished loading and the player is in the game.</summary>
        public event Action InGame;
        public event Action<string> LineParsed;

        string pendingMap;
        int linesSinceMap;

        public void Feed(string raw)
        {
            var line = Timestamp.Replace(raw.TrimEnd('\r', '\n', ' '), "", 1);
            LineParsed?.Invoke(line);

            if (pendingMap != null)
            {
                if (PlayersLine.IsMatch(line))
                {
                    var map = pendingMap.ToLowerInvariant();
                    pendingMap = null;
                    MapChanged?.Invoke(map);
                    return;
                }
                if (++linesSinceMap > 2) pendingMap = null;
            }

            var match = MapLine.Match(line);
            if (match.Success)
            {
                pendingMap = match.Groups[1].Value;
                linesSinceMap = 0;
            }
            else if (line == GameConfig.SaveMarker)
            {
                SaveRequested?.Invoke();
            }
            else if (line == "Redownloading all lightmaps")
            {
                InGame?.Invoke();
            }
            else if (line.StartsWith(GameConfig.ReadyMarker, StringComparison.Ordinal))
            {
                GameStarted?.Invoke();
            }
        }
    }
}
