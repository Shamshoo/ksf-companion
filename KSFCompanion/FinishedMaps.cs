using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace KsfCompanion
{
    /// <summary>
    /// The maps you've finished, for the nominate page - per player, tick (css / css100t) and style, kept on disk.
    /// ksf.surf lists them 5 at a time, so the whole list is read at most about once a day; maps you finish in the
    /// meantime are added as KSF Companion sees them.
    /// </summary>
    sealed class FinishedMaps
    {
        sealed class Set
        {
            public DateTime ReadAt = DateTime.MinValue;
            public readonly Dictionary<string, FinishedMap> Maps = new Dictionary<string, FinishedMap>(StringComparer.OrdinalIgnoreCase);
        }

        static readonly IReadOnlyDictionary<string, FinishedMap> None = new Dictionary<string, FinishedMap>();
        readonly string path;
        readonly Dictionary<string, Set> sets = new Dictionary<string, Set>(StringComparer.OrdinalIgnoreCase);

        public FinishedMaps(string path)
        {
            this.path = path;
            Load();
        }

        public static string Key(string steamId, string game, int style) =>
            steamId == null ? null : string.Join("|", steamId, game, style.ToString(CultureInfo.InvariantCulture));

        public IReadOnlyDictionary<string, FinishedMap> Of(string key) => key != null && sets.TryGetValue(key, out var set) ? set.Maps : None;

        /// <summary>When the whole list was last read from ksf.surf (MinValue: never).</summary>
        public DateTime ReadAt(string key) => key != null && sets.TryGetValue(key, out var set) ? set.ReadAt : DateTime.MinValue;

        public void MarkRead(string key) => SetFor(key).ReadAt = DateTime.Now;

        /// <summary>Maps from ksf.surf's list. Its times win, unless you've since done better in game (it lags behind).</summary>
        public void Merge(string key, IEnumerable<FinishedMap> maps)
        {
            var set = SetFor(key);
            foreach (var map in maps.Where(m => !string.IsNullOrEmpty(m.Map)))
            {
                var faster = set.Maps.TryGetValue(map.Map, out var known) && known.Time > 0 && known.Time < map.Time - 0.0005;
                set.Maps[map.Map] = new FinishedMap { Map = map.Map, Time = faster ? known.Time : map.Time, Group = map.Group, Points = map.Points };
            }
        }

        /// <summary>A finish seen in game or on the dashboard. True if the map is new here, or the time better.</summary>
        public bool Add(string key, string map, double time)
        {
            if (key == null || string.IsNullOrEmpty(map) || time <= 0) return false;
            var set = SetFor(key);
            if (set.Maps.TryGetValue(map, out var known) && known.Time > 0 && known.Time <= time + 0.0005) return false;
            set.Maps[map] = new FinishedMap { Map = map, Time = time, Group = known?.Group, Points = known?.Points };
            return true;
        }

        Set SetFor(string key)
        {
            if (!sets.TryGetValue(key, out var set)) sets[key] = set = new Set();
            return set;
        }

        void Load()
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var f = line.Split('\t');
                    // "#read", player|game|style, when the whole list was read (UTC ticks)
                    if (f.Length >= 3 && f[0] == "#read")
                    {
                        if (long.TryParse(f[2], out var ticks) && ticks > 0) SetFor(f[1]).ReadAt = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                        continue;
                    }
                    // player|game|style, map, time, group, points
                    if (f.Length < 5 || f[0].StartsWith("#", StringComparison.Ordinal)) continue;
                    SetFor(f[0]).Maps[f[1]] = new FinishedMap
                    {
                        Map = f[1],
                        Time = double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ? time : 0,
                        Group = int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) ? group : (int?)null,
                        Points = int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var points) ? points : (int?)null,
                    };
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Save()
        {
            var lines = new List<string>();
            foreach (var pair in sets.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(string.Join("\t", "#read", pair.Key,
                    pair.Value.ReadAt == DateTime.MinValue ? "0" : pair.Value.ReadAt.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)));
                lines.AddRange(pair.Value.Maps.Values.OrderBy(m => m.Map, StringComparer.OrdinalIgnoreCase).Select(m => string.Join("\t",
                    pair.Key, m.Map.Replace('\t', ' '), m.Time.ToString("R", CultureInfo.InvariantCulture),
                    m.Group?.ToString(CultureInfo.InvariantCulture) ?? "", m.Points?.ToString(CultureInfo.InvariantCulture) ?? "")));
            }
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
    }
}
