using System;

namespace KsfCompanion
{
    /// <summary>
    /// KSF's player titles - the tag in front of your name in chat - best first. The top ones go by rank (top 10,
    /// top 25...), the rest by points. Worked out from ksf.surf's ranking and profile pages (September 2026); KSF
    /// doesn't publish the list anywhere but the in-game !ranks.
    /// </summary>
    static class KsfLevels
    {
        public sealed class Level
        {
            public Level(string title, int? topRank, int minPoints)
            {
                Title = title;
                TopRank = topRank;
                MinPoints = minPoints;
            }

            public string Title { get; }
            /// <summary>Rank titles: this rank or better. Null for points titles.</summary>
            public int? TopRank { get; }
            /// <summary>Points titles: at least this many points.</summary>
            public int MinPoints { get; }
        }

        public static readonly Level[] Ladder =
        {
            new Level("MASTER", 10, 0),
            new Level("ELITE", 25, 0),
            new Level("VETERAN", 50, 0),
            new Level("PRO", 100, 0),
            new Level("EXPERT", 200, 0),
            new Level("HOTSHOT", 300, 0),
            new Level("EXCEPTIONAL", 500, 0),
            new Level("SEASONED", 750, 0),
            new Level("EXPERIENCED", 1500, 0),
            new Level("ACCOMPLISHED", null, 13000),
            new Level("ADEPT", null, 9000),
            new Level("PROFICIENT", null, 6000),
            new Level("SKILLED", null, 4000),
            new Level("CASUAL", null, 2500),
            new Level("BEGINNER", null, 1000),
            new Level("ROOKIE", null, int.MinValue),
        };

        /// <summary>
        /// Where a player is on the ladder: ksf.surf's own title for them (it's updated as they play, so it can lag a
        /// little), or what their rank and points make them if that's better.
        /// </summary>
        public static int IndexOf(PlayerStanding standing)
        {
            var index = IndexOf(standing.Rank, standing.Points);
            var shown = string.IsNullOrEmpty(standing.Title) ? -1
                : Array.FindIndex(Ladder, l => string.Equals(l.Title, standing.Title, StringComparison.OrdinalIgnoreCase));
            return shown >= 0 ? Math.Min(index, shown) : index;
        }

        /// <summary>Where a rank and points put you on the ladder (0 = the best title).</summary>
        public static int IndexOf(int? rank, int points)
        {
            for (var i = 0; i < Ladder.Length; i++)
            {
                var level = Ladder[i];
                if (level.TopRank is int top ? rank is int r && r > 0 && r <= top : points >= level.MinPoints) return i;
            }
            return Ladder.Length - 1;
        }
    }
}
