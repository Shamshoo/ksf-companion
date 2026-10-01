using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace KsfCompanion
{
    /// <summary>
    /// ksf.surf's list of every KSF map (tier, stages, bonuses, rating, mappers...) for the nominate page, kept on
    /// disk. Getting it takes about 90 small requests, so it's fetched once in the background and then only topped
    /// up with the newest maps.
    /// </summary>
    sealed class MapCatalog
    {
        readonly string path;
        readonly Dictionary<string, MapInfo> maps = new Dictionary<string, MapInfo>(StringComparer.OrdinalIgnoreCase);

        public MapCatalog(string path)
        {
            this.path = path;
            Load();
        }

        public int Count => maps.Count;
        public List<MapInfo> Maps => maps.Values.ToList();
        /// <summary>When the whole list was last read to its end (MinValue: never, or only partly).</summary>
        public DateTime CompleteAt { get; private set; } = DateTime.MinValue;
        public DateTime ToppedUpAt { get; private set; } = DateTime.MinValue;

        public bool Has(string name) => maps.ContainsKey(name);
        public MapInfo Find(string name) => name != null && maps.TryGetValue(name, out var map) ? map : null;

        public void Add(IEnumerable<MapInfo> list)
        {
            foreach (var map in list.Where(m => !string.IsNullOrEmpty(m.Name))) maps[map.Name] = map;
        }

        public void MarkComplete() => CompleteAt = ToppedUpAt = DateTime.Now;
        public void MarkToppedUp() => ToppedUpAt = DateTime.Now;

        void Load()
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var f = line.Split('\t');
                    if (f.Length >= 3 && f[0] == "#complete" && long.TryParse(f[1], out var complete) && long.TryParse(f[2], out var topped))
                    {
                        CompleteAt = complete > 0 ? new DateTime(complete, DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue;
                        ToppedUpAt = topped > 0 ? new DateTime(topped, DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue;
                        continue;
                    }
                    // name, tier, linear, stages, bonuses, rating, rating count, popularity, added (UTC ticks), mappers
                    if (f.Length < 10 || f[0].StartsWith("#", StringComparison.Ordinal)) continue;
                    maps[f[0]] = new MapInfo
                    {
                        Name = f[0],
                        Tier = Int(f[1]),
                        IsLinear = f[2] == "1",
                        StageCount = Int(f[3]),
                        BonusCount = Int(f[4]),
                        Rating = double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) ? rating : (double?)null,
                        RatingCount = Int(f[6]),
                        Popularity = double.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var popularity) ? popularity : 0,
                        Added = long.TryParse(f[8], out var added) && added > 0 ? new DateTime(added, DateTimeKind.Utc).ToLocalTime() : (DateTime?)null,
                        Mappers = f[9],
                    };
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Save()
        {
            var lines = new List<string>
            {
                string.Join("\t", "#complete", Ticks(CompleteAt), Ticks(ToppedUpAt)),
            };
            lines.AddRange(maps.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m => string.Join("\t",
                Clean(m.Name), m.Tier.ToString(CultureInfo.InvariantCulture), m.IsLinear ? "1" : "0",
                m.StageCount.ToString(CultureInfo.InvariantCulture), m.BonusCount.ToString(CultureInfo.InvariantCulture),
                m.Rating?.ToString("R", CultureInfo.InvariantCulture) ?? "", m.RatingCount.ToString(CultureInfo.InvariantCulture),
                m.Popularity.ToString("R", CultureInfo.InvariantCulture),
                m.Added is DateTime added ? added.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) : "0",
                Clean(m.Mappers))));
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

        static int Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        static string Ticks(DateTime at) => at == DateTime.MinValue ? "0" : at.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        static string Clean(string text) => (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
