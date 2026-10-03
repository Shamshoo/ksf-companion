using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KsfCompanion
{
    /// <summary>
    /// Reads the KSF timer's on-screen text (which stage you're on, "Finished [Stage 3]: 00:17:06") while you play.
    /// The game never prints that text to the console, but it does save it in demos, so the app has the game record
    /// one with its own "record" command and reads the file as it grows. Only a file is read: nothing touches the
    /// game's memory.
    /// </summary>
    sealed class LiveHud
    {
        public const string DemoName = "ksfc_live";
        const int Overlap = 2048, MaxRead = 1 << 20;
        // "- Stage 4 -" (right-hand panel), "[Zone: Stage 4 Start]" (centre text)
        static readonly Regex PanelZone = new Regex(@"^- (?<kind>Stage|Bonus) (?<n>\d+)\b", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        static readonly Regex StartZone = new Regex(@"\[Zone: (?<kind>Stage|Bonus) (?<n>\d+)", RegexOptions.CultureInvariant);
        // "Finished [Stage 3]: 00:17:06" then "(WR +00:02:09)" on the next line
        static readonly Regex Finished = new Regex(@"Finished \[(?<kind>Stage|Bonus) (?<n>\d+)[^\]]*\]: (?<time>\d+(?::\d{2}){1,2}(?:[:.]\d{1,3})?)",
            RegexOptions.CultureInvariant);
        // "Timeleft: 8 minutes" / "Timeleft: 1 minute" / "Timeleft: Less than 1 minute" (top of the right-hand panel)
        static readonly Regex PanelTimeLeft = new Regex(@"Timeleft: (?:(?<n>\d+) minutes?|(?<less>Less than 1 minute))", RegexOptions.CultureInvariant);
        static readonly string[] Markers = { "Finished [", "- Stage ", "- Bonus ", "[Zone: ", "Timeleft: " };
        // The game's spectator screen being shown (1) or hidden (0): the name, its end, then the show flag.
        const string SpectatorPanel = "specgui\0";

        readonly string folder;
        string path;
        long position = -1;
        byte[] carry = new byte[0];
        string lastFinish;
        int? lastZone, shownMinutes;
        // False until we've seen the demo missing: a demo that's already there when we start is old news.
        bool sawNoDemo;
        // Just back from spectating: the finish still on screen happened before.
        bool finishIsOld;

        /// <param name="cstrikeDir">Where the game saves demos.</param>
        public LiveHud(string cstrikeDir)
        {
            folder = cstrikeDir;
            path = Path.Combine(cstrikeDir, DemoName + ".dem");
        }

        public string DemoPath => path;
        /// <summary>When the demo last grew: the game is recording it.</summary>
        public DateTime LastDataAt { get; private set; }
        /// <summary>
        /// Whether you're spectating (null: not seen yet). The timer's text then belongs to whoever you watch, so
        /// its finishes aren't yours.
        /// </summary>
        public bool? Spectating { get; private set; }
        /// <summary>When the timer's text last showed your stage/bonus - even the same one again - so it's known to be current.</summary>
        public DateTime LastZoneSeenAt { get; private set; }

        /// <summary>
        /// Follow this demo from now on. The game never overwrites a demo - "record ksfc_live" writes ksfc_live_2.dem
        /// when ksfc_live.dem is still there - so the app follows whatever name the game says it's recording to.
        /// </summary>
        public void Watch(string demoPath, bool alreadyRunning)
        {
            if (string.Equals(path, demoPath, StringComparison.OrdinalIgnoreCase)) return;
            path = demoPath;
            position = -1;
            carry = new byte[0];
            lastFinish = null;
            lastZone = shownMinutes = null;
            Spectating = null;
            finishIsOld = false;
            sawNoDemo = !alreadyRunning;
        }

        /// <summary>
        /// One of our demos that the game is recording right now, or null. The game keeps the file open for writing
        /// the whole time - even while you stand still and nothing new gets written for a while - and closes it the
        /// moment the recording ends, so that's what tells.
        /// </summary>
        public string FindRecordingDemo()
        {
            try
            {
                return new DirectoryInfo(folder).GetFiles(DemoName + "*.dem")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault(f => IsBeingWritten(f.FullName))?.FullName;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// Whether the game has the file open (it holds the demo it's recording open the whole time). Linux doesn't lock
        /// files the way Windows does, so this looks at the files the game has open instead.
        /// </summary>
        static bool IsBeingWritten(string file)
        {
            var pid = GameBridge.FindGameProcessId();
            if (pid == 0) return false;
            // The game's own path to it may go another way (a library folder reached through a link): the demo's name
            // in a cstrike folder is enough, there's only one game.
            var name = "/" + Path.GetFileName(file);
            try
            {
                foreach (var fd in Directory.EnumerateFileSystemEntries($"/proc/{pid}/fd"))
                {
                    try
                    {
                        if (new FileInfo(fd).LinkTarget?.EndsWith(name, StringComparison.Ordinal) == true) return true;
                    }
                    catch (IOException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return false;
        }

        /// <summary>Removes our finished demos, so the next recording gets the plain name again. The one being recorded stays.</summary>
        public void DeleteFinishedDemos()
        {
            try
            {
                foreach (var file in Directory.GetFiles(folder, DemoName + "*.dem"))
                {
                    // Deleting it would leave the game writing into a file nobody can read any more.
                    if (IsBeingWritten(file)) continue;
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>The map a demo is of, from its header ("HL2DEMO", versions, server and client name, then the map), or null (e.g. not written yet).</summary>
        public static string MapOf(string demoPath)
        {
            const int mapAt = 8 + 4 + 4 + 260 + 260, mapLength = 260;
            try
            {
                using var stream = new FileStream(demoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var header = new byte[mapAt + mapLength];
                var read = 0;
                while (read < header.Length)
                {
                    var n = stream.Read(header, read, header.Length - read);
                    if (n <= 0) return null;
                    read += n;
                }
                if (Encoding.ASCII.GetString(header, 0, 7) != "HL2DEMO") return null;
                var end = Array.IndexOf(header, (byte)0, mapAt);
                return Encoding.ASCII.GetString(header, mapAt, (end < 0 ? header.Length : end) - mapAt);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>You're on this zone now (1-30 stage, 31+ bonus).</summary>
        public event Action<int> ZoneChanged;
        /// <summary>A stage or bonus was just finished: zone, time in seconds, and whether it happened just now (not old text read on start-up).</summary>
        public event Action<int, double, bool> ZoneFinished;
        /// <summary>
        /// The panel's "Timeleft: N minutes" (N = 0: "Less than 1 minute"), once for each bit of the demo that has it:
        /// N, what it showed before it changed to N (N itself when it didn't just change; null the first time), and
        /// roughly how many seconds ago it changed, when that was just now (null otherwise).
        /// </summary>
        public event Action<int, int?, double?> TimeLeftShown;

        /// <summary>For testing on a finished demo: read all of it, as if it had been recorded while we watched.</summary>
        public void ReadWholeDemo()
        {
            sawNoDemo = true;
            while (Poll()) { }
        }

        /// <summary>Reads what the game added to the demo since last time (up to 1 MB). Cheap when nothing changed. True if there's more.</summary>
        public bool Poll()
        {
            long length;
            try
            {
                if (!File.Exists(path))
                {
                    position = -1;
                    sawNoDemo = true;
                    return false;
                }
                length = new FileInfo(path).Length;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }

            var catchingUp = false;
            if (position < 0 || length < position)
            {
                // A demo that was already there when the app started (only its latest part matters, and nothing
                // in it is "just now"), or a fresh recording - a new map - that replaced the old file.
                catchingUp = position < 0 && !sawNoDemo;
                position = catchingUp ? Math.Max(0, length - MaxRead) : 0;
                carry = new byte[0];
                lastFinish = null;
                lastZone = shownMinutes = null;
                sawNoDemo = true;
            }
            if (length == position) return false;

            byte[] chunk;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(position, SeekOrigin.Begin);
                chunk = new byte[Math.Min(length - position, MaxRead)];
                var read = 0;
                while (read < chunk.Length)
                {
                    var n = stream.Read(chunk, read, chunk.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < chunk.Length) Array.Resize(ref chunk, read);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            if (chunk.Length == 0) return false;

            position += chunk.Length;
            // The new bytes cover the game time since the demo last grew (the game writes it every few seconds).
            double? span = LastDataAt == default ? (double?)null : (DateTime.Now - LastDataAt).TotalSeconds;
            LastDataAt = DateTime.Now;
            var data = new byte[carry.Length + chunk.Length];
            Buffer.BlockCopy(carry, 0, data, 0, carry.Length);
            Buffer.BlockCopy(chunk, 0, data, carry.Length, chunk.Length);
            var newFrom = carry.Length;
            carry = data.Length > Overlap ? data.Skip(data.Length - Overlap).ToArray() : data;
            Scan(data, catchingUp, newFrom, catchingUp || position < length ? null : span);
            return position < length;
        }

        /// <summary>
        /// The game packs its network messages bit by bit, so text starts at any bit offset: look at the bytes from
        /// all 8 bit positions and read the timer's text wherever it appears, in the order it was sent.
        /// <paramref name="newFrom"/> is where the bytes not seen before start, and <paramref name="span"/> how many
        /// seconds of play they cover (when they're live), to tell roughly when something in them happened.
        /// </summary>
        void Scan(byte[] data, bool catchingUp, int newFrom = 0, double? span = null)
        {
            int? minutesBefore = null;
            var minutesChangedAt = -1;
            var minutesSeen = false;
            var found = new List<(int Offset, string Text)>();
            for (var shift = 0; shift < 8; shift++)
            {
                var text = Latin1(Shift(data, shift));
                foreach (var marker in Markers)
                    for (var at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
                    {
                        // The whole HUD message is printable text: take the run of it around the marker.
                        int start = at, end = at;
                        while (start > 0 && IsText(text[start - 1])) start--;
                        while (end < text.Length && IsText(text[end])) end++;
                        found.Add((start, text.Substring(start, end - start)));
                        at = end - marker.Length;
                    }
                for (var at = text.IndexOf(SpectatorPanel, StringComparison.Ordinal); at >= 0 && at + SpectatorPanel.Length < text.Length;
                     at = text.IndexOf(SpectatorPanel, at + 1, StringComparison.Ordinal))
                {
                    var flag = text[at + SpectatorPanel.Length];
                    if (flag == '\u0000' || flag == '\u0001') found.Add((at, flag == '\u0001' ? "\u0000spectating" : "\u0000playing"));
                }
            }

            foreach (var (offset, text) in found.OrderBy(f => f.Offset).Distinct())
            {
                // Time left is the server's, whoever's panel it is.
                var shown = PanelTimeLeft.Match(text);
                if (shown.Success)
                {
                    var minutes = shown.Groups["less"].Success ? 0 : int.Parse(shown.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (minutes != shownMinutes)
                    {
                        minutesBefore = shownMinutes;
                        minutesChangedAt = offset;
                        shownMinutes = minutes;
                    }
                    minutesSeen = true;
                }

                if (text[0] == '\u0000')
                {
                    var spectating = text == "\u0000spectating";
                    // Back in the game: the zone text is yours again, and a finish still on screen is from before.
                    if (Spectating == true && !spectating)
                    {
                        lastZone = null;
                        finishIsOld = true;
                    }
                    Spectating = spectating;
                    continue;
                }
                // While you spectate, the timer's text is about whoever you're watching.
                var watchingSomeone = Spectating == true;

                var zone = PanelZone.Match(text);
                if (!zone.Success) zone = StartZone.Match(text);
                if (zone.Success && !watchingSomeone)
                {
                    var id = ZoneId(zone.Groups["kind"].Value, zone.Groups["n"].Value);
                    if (!catchingUp) LastZoneSeenAt = DateTime.Now;
                    if (id != lastZone)
                    {
                        lastZone = id;
                        ZoneChanged?.Invoke(id);
                    }
                }

                var finish = Finished.Match(text);
                if (finish.Success)
                {
                    var key = finish.Groups["kind"].Value + finish.Groups["n"].Value + "=" + finish.Groups["time"].Value;
                    if (key != lastFinish && TryParseTime(finish.Groups["time"].Value, out var seconds))
                    {
                        lastFinish = key;
                        if (!watchingSomeone)
                        {
                            ZoneFinished?.Invoke(ZoneId(finish.Groups["kind"].Value, finish.Groups["n"].Value), seconds, !catchingUp && !finishIsOld);
                            finishIsOld = false;
                        }
                    }
                }
                else if (!watchingSomeone && text.Contains("Time:"))
                {
                    // The centre text without a finish on it: any finish from here on is new.
                    finishIsOld = false;
                }
            }

            if (!minutesSeen) return;
            var changed = minutesChangedAt >= 0;
            double? changedAgo = null;
            if (changed && minutesBefore != null && !catchingUp && span is double covered)
            {
                // Where the new number first shows in the new bytes says how far into those seconds it changed.
                var through = (double)(minutesChangedAt - newFrom) / Math.Max(1, data.Length - newFrom);
                changedAgo = (1 - Math.Max(0, Math.Min(1, through))) * Math.Min(covered, 10) + 0.5;
            }
            TimeLeftShown?.Invoke(shownMinutes.Value, changed ? minutesBefore : shownMinutes, changedAgo);
        }

        static int ZoneId(string kind, string number) =>
            (kind == "Bonus" ? MapReport.FirstBonusZone - 1 : 0) + int.Parse(number, System.Globalization.CultureInfo.InvariantCulture);

        static bool IsText(char c) => (c >= ' ' && c <= '~') || c == '\n' || c >= ' ';

        /// <summary>The bytes as they'd read if the data started <paramref name="shift"/> bits later (bits are packed LSB first).</summary>
        static byte[] Shift(byte[] data, int shift)
        {
            if (shift == 0) return data;
            var result = new byte[data.Length - 1];
            for (var i = 0; i < result.Length; i++)
                result[i] = (byte)((data[i] >> shift) | (data[i + 1] << (8 - shift)));
            return result;
        }

        static string Latin1(byte[] bytes)
        {
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
            return new string(chars);
        }

        /// <summary>The timer's "00:17:06" is minutes:seconds:hundredths (hours come first on very long runs).</summary>
        public static bool TryParseTime(string text, out double seconds)
        {
            seconds = 0;
            var parts = text.Split(':', '.');
            if (parts.Length < 2) return false;
            var fraction = parts[parts.Length - 1];
            if (!int.TryParse(fraction, out var hundredths)) return false;
            seconds = hundredths / Math.Pow(10, fraction.Length);
            var unit = 1;
            for (var i = parts.Length - 2; i >= 0; i--, unit *= 60)
            {
                if (!int.TryParse(parts[i], out var value)) return false;
                seconds += value * unit;
            }
            return true;
        }

        /// <summary>
        /// Removes KSF Companion's demos (ksfc_*.dem) once the game has closed - they're only needed while playing.
        /// Quietly leaves any the game still has open.
        /// </summary>
        public void Delete()
        {
            try
            {
                foreach (var file in Directory.GetFiles(folder, "ksfc_*.dem"))
                {
                    if (IsBeingWritten(file)) continue;
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            position = -1;
        }
    }
}
