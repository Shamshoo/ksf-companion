using System;
using System.Linq;
using System.Text;

namespace KsfCompanion.Ui
{
    /// <summary>
    /// Map search on the nominate page: "lieden", "surf_leiden", "utopia njv" all find what you meant. The surf_ in
    /// front and the _ and - in names don't matter, and a word that's a letter off (a typo, two letters swapped)
    /// still finds the map - after the maps that match exactly.
    /// </summary>
    static class MapMatch
    {
        /// <summary>Score of a close (not exact) match: anything at or above this is "did you mean".</summary>
        public const int Close = 10;

        /// <summary>What was typed, as words to look for: lower case, without surf_ and the separators.</summary>
        public static string[] Words(string text) =>
            (text ?? "").ToLowerInvariant()
                .Split(new[] { ' ', '_', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Compact)
                .Where(w => w.Length > 0 && w != "surf")
                .ToArray();

        /// <summary>How well a map matches the words: null = not at all, lower = better (0 = its name starts with them).</summary>
        public static int? Score(string name, string mappers, string[] words)
        {
            var lower = (name ?? "").ToLowerInvariant();
            var bare = Compact(lower.StartsWith("surf_", StringComparison.Ordinal) ? lower.Substring(5) : lower);
            var by = (mappers ?? "").ToLowerInvariant();
            var total = 0;
            for (var i = 0; i < words.Length; i++)
            {
                var w = words[i];
                int score;
                if (bare.StartsWith(w, StringComparison.Ordinal)) score = i == 0 ? 0 : 1;
                else if (bare.Contains(w)) score = 2;
                else if (w.Length >= 2 && by.Contains(w)) score = 3;
                else
                {
                    // Short words have to be spot on: one letter off in three matches half the list.
                    var allowed = w.Length >= 7 ? 2 : w.Length >= 4 ? 1 : 0;
                    if (allowed == 0) return null;
                    var off = Distance(w, bare, allowed);
                    if (off > allowed) return null;
                    score = Close * off;
                }
                total += score;
            }
            return total;
        }

        static string Compact(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// How many letters have to change (typed wrong, missing, extra, or two swapped) for the word to appear
        /// somewhere in the name; stops counting past <paramref name="max"/>.
        /// </summary>
        static int Distance(string word, string name, int max)
        {
            int m = word.Length, n = name.Length;
            // Row i: the first i letters of the word against the name up to j; row 0 is free, so it may start anywhere.
            var before = new int[n + 1];
            var previous = new int[n + 1];
            var current = new int[n + 1];
            for (var i = 1; i <= m; i++)
            {
                current[0] = i;
                var rowBest = i;
                for (var j = 1; j <= n; j++)
                {
                    var cost = word[i - 1] == name[j - 1] ? 0 : 1;
                    var value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                    if (i > 1 && j > 1 && word[i - 1] == name[j - 2] && word[i - 2] == name[j - 1])
                        value = Math.Min(value, before[j - 2] + 1);
                    current[j] = value;
                    if (value < rowBest) rowBest = value;
                }
                if (rowBest > max) return max + 1;
                var spare = before;
                before = previous;
                previous = current;
                current = spare;
            }
            return previous.Min();
        }
    }
}
