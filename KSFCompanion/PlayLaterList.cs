using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KsfCompanion
{
    sealed class PlayLaterEntry
    {
        public string Map;
        public int? Tier;
        public DateTime Saved = DateTime.MinValue;
    }

    /// <summary>
    /// The saved maps, newest first, in a text file that can also be edited by hand:
    /// <c>surf_boreas | T1 | saved 2026-09-27 01:12</c>
    /// </summary>
    sealed class PlayLaterList
    {
        const string SavedFormat = "yyyy-MM-dd HH:mm";
        static readonly Regex MapName = new Regex(@"^[A-Za-z0-9_\-.]+$");

        readonly string path;
        List<PlayLaterEntry> items = new List<PlayLaterEntry>();
        DateTime loadedStamp = DateTime.MinValue;

        public PlayLaterList(string path)
        {
            this.path = path;
            if (File.Exists(path)) Reload();
            else Save();
        }

        public string FilePath => path;

        public IReadOnlyList<PlayLaterEntry> Items
        {
            get
            {
                Reload();
                return items;
            }
        }

        public bool Contains(string map) => map != null && Items.Any(e => e.Map.Equals(map, StringComparison.OrdinalIgnoreCase));

        public bool Add(string map, int? tier)
        {
            if (Contains(map)) return false;
            items.Insert(0, new PlayLaterEntry { Map = map.ToLowerInvariant(), Tier = tier, Saved = DateTime.Now });
            Save();
            return true;
        }

        public void SetTier(string map, int tier)
        {
            Reload();
            var entry = items.FirstOrDefault(e => e.Map.Equals(map, StringComparison.OrdinalIgnoreCase));
            if (entry == null || entry.Tier == tier) return;
            entry.Tier = tier;
            Save();
        }

        public bool Remove(string map)
        {
            Reload();
            if (items.RemoveAll(e => e.Map.Equals(map, StringComparison.OrdinalIgnoreCase)) == 0) return false;
            Save();
            return true;
        }

        /// <summary>Picks up edits made in Notepad since the last read.</summary>
        void Reload()
        {
            DateTime stamp;
            try { stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
            catch (IOException) { return; }
            if (stamp == loadedStamp) return;

            string[] lines;
            try { lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0]; }
            catch (IOException) { return; }

            var loaded = new List<PlayLaterEntry>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|').Select(p => p.Trim()).ToArray();
                if (!MapName.IsMatch(parts[0])) continue;

                var entry = new PlayLaterEntry { Map = parts[0].ToLowerInvariant() };
                foreach (var part in parts.Skip(1))
                {
                    if (part.Length > 1 && char.ToUpperInvariant(part[0]) == 'T' && int.TryParse(part.Substring(1), out var tier))
                        entry.Tier = tier;
                    else if (part.StartsWith("saved ", StringComparison.OrdinalIgnoreCase) &&
                             DateTime.TryParseExact(part.Substring(6), SavedFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var saved))
                        entry.Saved = saved;
                }
                if (!loaded.Any(e => e.Map == entry.Map)) loaded.Add(entry);
            }
            items = loaded;
            loadedStamp = stamp;
        }

        void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# KSF Companion play-later list, newest first. One map per line - you can edit or reorder this file.");
            foreach (var e in items)
            {
                sb.Append(e.Map);
                if (e.Tier is int tier) sb.Append(" | T").Append(tier);
                if (e.Saved > DateTime.MinValue) sb.Append(" | saved ").Append(e.Saved.ToString(SavedFormat, CultureInfo.InvariantCulture));
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
            loadedStamp = File.GetLastWriteTimeUtc(path);
        }
    }
}
