using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace KsfCompanion
{
    /// <summary>
    /// The record on each stage and bonus of the maps you've played, kept on disk for a day. KSF only has a
    /// leaderboard per zone, so getting them all takes one request per zone - this way that happens once, not on
    /// every visit.
    /// </summary>
    sealed class ZoneRecordStore
    {
        static readonly TimeSpan KeepOnDisk = TimeSpan.FromDays(7);
        readonly string path;
        readonly object gate = new object();
        // key: game|style|map|zone; a null record means KSF has no time on that zone yet.
        readonly Dictionary<string, (DateTime At, WorldRecord Wr)> entries = new Dictionary<string, (DateTime, WorldRecord)>(StringComparer.OrdinalIgnoreCase);

        public ZoneRecordStore(string path)
        {
            this.path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    // game, style, map, zone, saved (UTC ticks), time, steamid, name
                    var f = line.Split(new[] { '\t' }, 8);
                    if (f.Length < 8 || !long.TryParse(f[4], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) continue;
                    var at = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                    if (DateTime.Now - at > KeepOnDisk) continue;
                    var wr = double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var time)
                        ? new WorldRecord { Rank = 1, Time = time, SteamId = f[6], Name = f[7] }
                        : null;
                    entries[Key(f[0], f[1], f[2], f[3])] = (at, wr);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static string Key(string game, string style, string map, string zone) => $"{game}|{style}|{map}|{zone}";
        static string Key(string game, int style, string map, int zone) =>
            Key(game, style.ToString(CultureInfo.InvariantCulture), map, zone.ToString(CultureInfo.InvariantCulture));

        /// <summary>The records known for these zones that are younger than maxAge (a copy you can change).</summary>
        public Dictionary<int, WorldRecord> Get(string game, int style, string map, IEnumerable<int> zones, TimeSpan maxAge)
        {
            var found = new Dictionary<int, WorldRecord>();
            lock (gate)
                foreach (var zone in zones)
                    if (entries.TryGetValue(Key(game, style, map, zone), out var e) && e.Wr != null && DateTime.Now - e.At < maxAge)
                        found[zone] = e.Wr;
            return found;
        }

        /// <summary>True when we asked KSF about this zone recently (even if nobody has a time on it).</summary>
        public bool IsFresh(string game, int style, string map, int zone, TimeSpan maxAge)
        {
            lock (gate)
                return entries.TryGetValue(Key(game, style, map, zone), out var e) && DateTime.Now - e.At < maxAge;
        }

        public void Put(string game, int style, string map, int zone, WorldRecord wr)
        {
            lock (gate) entries[Key(game, style, map, zone)] = (DateTime.Now, wr);
        }

        public void Save()
        {
            string[] lines;
            lock (gate)
                lines = entries.Where(e => DateTime.Now - e.Value.At < KeepOnDisk).Select(e =>
                {
                    var k = e.Key.Split('|');
                    var wr = e.Value.Wr;
                    return string.Join("\t", k[0], k[1], k[2], k[3], e.Value.At.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                        wr?.Time.ToString("R", CultureInfo.InvariantCulture) ?? "", Clean(wr?.SteamId), Clean(wr?.Name));
                }).ToArray();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + ".tmp";
                File.WriteAllLines(temp, lines, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static string Clean(string text) => (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
