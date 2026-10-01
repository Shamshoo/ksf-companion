using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KsfCompanion
{
    sealed class KeyNames
    {
        public string Save, Card, List;

        public static KeyNames From(Settings s) => new KeyNames
        {
            Save = GameConfig.ValidKey(s.Get("key_save"), "F5"),
            Card = GameConfig.ValidKey(s.Get("key_card"), "F6"),
            List = GameConfig.ValidKey(s.Get("key_list"), "F7"),
        };
    }

    /// <summary>Turns KSF data into the short text lines the card key prints into the game console.</summary>
    static class CardBuilder
    {
        const string Sep = "  -  ";

        public static List<string> Build(MapReport r, bool saved, KeyNames keys, bool includeHint = true)
        {
            var lines = new List<string>();

            if (!r.IsOnKsf)
            {
                lines.Add(r.Map + Sep + (r.Error ?? "not on KSF's map list"));
                if (r.Error == null && r.Suggestions.Count > 0) lines.Add("KSF has: " + string.Join(", ", r.Suggestions));
                if (includeHint) lines.Add(Hint(saved, keys));
                return lines;
            }

            var info = r.Info;
            var head = new List<string> { info.Name, "Tier " + info.Tier, info.IsLinear ? "linear" : Plural(info.StageCount, "stage") };
            var bonuses = info.BonusCount == 0 ? "no bonuses" : Plural(info.BonusCount, "bonus", "bonuses");
            if (info.BonusCount > 0 && r.Zones.Count > 0) bonuses += $" ({r.BonusesDone} done)";
            head.Add(bonuses);
            if (!string.IsNullOrEmpty(info.Mappers)) head.Add("by " + info.Mappers);
            if (r.Game == "css100t") head.Add("100 tick");
            lines.Add(string.Join(Sep, head));

            if (r.Wr != null) lines.Add($"WR    {Format.Time(r.Wr.Time)}   {r.Wr.Name}");
            else lines.Add(r.Error != null ? "WR    (" + r.Error + ")" : "WR    nobody has finished it yet");

            var me = r.Main;
            if (r.SteamId == null) lines.Add("you   (couldn't tell which Steam account you're on - set steamid in settings.ini)");
            else if (r.PersonalError != null) lines.Add("you   (" + r.PersonalError + ")");
            else if (me?.Time is double time)
            {
                var bits = new List<string> { "you   " + Format.Time(time) };
                if (me.Rank > 0 && me.TotalRanks > 0) bits.Add($"rank {me.Rank}/{me.TotalRanks}");
                if (me.Group > 0) bits.Add("group " + me.Group);
                if (r.Wr != null)
                    bits.Add(string.Equals(r.Wr.SteamId, r.SteamId, StringComparison.OrdinalIgnoreCase)
                        ? "that's the WR!"
                        : Format.Diff(time - r.Wr.Time) + " behind WR");
                lines.Add(string.Join("   ", bits));
            }
            else
            {
                lines.Add("you   not finished yet" + (me?.TotalRanks > 0 ? $"  ({me.TotalRanks} players have)" : ""));
            }

            if (r.SteamId != null && r.PersonalError == null)
            {
                if (r.IsStaged) lines.AddRange(ZoneLines(r, "stages", Enumerable.Range(1, info.StageCount).ToList()));
                if (info.BonusCount > 0) lines.AddRange(ZoneLines(r, "bonuses", Enumerable.Range(MapReport.FirstBonusZone, info.BonusCount).ToList()));
            }

            var stats = new List<string>();
            if (me?.Completions > 0) stats.Add(Plural(me.Completions.Value, "finish", "finishes"));
            if (me?.Attempts > 0) stats.Add(Plural(me.Attempts.Value, "attempt"));
            if (me?.PlaytimeSeconds > 0) stats.Add(Format.Duration(me.PlaytimeSeconds.Value) + " played on this map");
            if (stats.Count > 0) lines.Add(string.Join(Sep, stats));

            if (includeHint) lines.Add(Hint(saved, keys));
            return lines;
        }

        public static List<string> BuildList(IReadOnlyList<PlayLaterEntry> items, string currentMap, KeyNames keys)
        {
            const int shown = 10;
            var lines = new List<string>();
            if (items.Count == 0)
            {
                lines.Add($"your play-later list is empty - press {keys.Save} on a map to save it");
                return lines;
            }

            lines.Add($"play later ({Plural(items.Count, "map")})");
            for (int i = 0; i < Math.Min(shown, items.Count); i++)
            {
                var e = items[i];
                var line = $"{i + 1,2}. {e.Map}";
                if (e.Tier is int tier) line += "   T" + tier;
                if (e.Saved > DateTime.MinValue) line += "   saved " + e.Saved.ToString("MMM d", CultureInfo.InvariantCulture);
                if (string.Equals(e.Map, currentMap, StringComparison.OrdinalIgnoreCase)) line += "   <- playing now";
                lines.Add(line);
            }
            if (items.Count > shown) lines.Add($"    ...and {items.Count - shown} more in the KSF Companion window");
            lines.Add("on KSF, type !nominate <map> in chat to vote one in");
            return lines;
        }

        /// <summary>
        /// Your best on each stage (or bonus), how far off its record it is and where you are on its leaderboard,
        /// four to a line: "S1 4.167 +0.458 #4570/31958".
        /// </summary>
        static IEnumerable<string> ZoneLines(MapReport r, string title, List<int> zones)
        {
            const int perLine = 4;
            var parts = new List<string>();
            double sum = 0;
            var allDone = true;
            foreach (var zone in zones)
            {
                r.ZoneWrs.TryGetValue(zone, out var wr);
                var mine = r.Zone(zone);
                if (mine?.Time is double time)
                {
                    sum += time;
                    var gap = wr == null ? "" : time <= wr.Time + 0.0005 ? " WR" : " " + Format.Diff(time - wr.Time);
                    var rank = mine.Unsynced ? " (new)" : mine.Rank > 0 && mine.TotalRanks > 0 ? $" #{mine.Rank}/{mine.TotalRanks}" : "";
                    parts.Add($"{MapReport.ZoneLabel(zone)} {Format.Short(time)}{gap}{rank}");
                }
                else
                {
                    allDone = false;
                    parts.Add($"{MapReport.ZoneLabel(zone)} --");
                }
            }
            var head = title.PadRight(9);
            for (var i = 0; i < parts.Count; i += perLine)
                yield return (i == 0 ? head : new string(' ', head.Length)) + string.Join("     ", parts.Skip(i).Take(perLine));
            if (allDone && zones.Count > 1 && !MapReport.IsBonus(zones[0])) yield return new string(' ', head.Length) + "sum of your best stages " + Format.Time(sum);
        }

        static string Hint(bool saved, KeyNames keys) => saved
            ? $"* in your play-later list *   hold {keys.List} to see the list"
            : $"{keys.Save} = save for later   hold {keys.Card} = this card   hold {keys.List} = play-later list";

        static string Plural(int n, string one, string many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";
    }

    static class Format
    {
        public static string Time(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:000}", (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:000}", t.Minutes, t.Seconds, t.Milliseconds);
        }

        /// <summary>For stage-length times: "4.167", "35.430", and minutes only when needed ("1:02.345").</summary>
        public static string Short(double seconds) =>
            seconds < 59.9995 ? seconds.ToString("0.000", CultureInfo.InvariantCulture) : Time(seconds);

        public static string Diff(double seconds)
        {
            var sign = seconds < 0 ? "-" : "+";
            var abs = Math.Abs(seconds);
            return sign + (abs >= 60 ? Time(abs) : abs.ToString("0.000", CultureInfo.InvariantCulture));
        }

        public static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            return t.TotalMinutes >= 1 ? $"{t.Minutes}m" : "<1m";
        }
    }
}
